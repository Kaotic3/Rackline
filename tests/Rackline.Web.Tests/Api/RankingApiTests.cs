using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using System.Text.Json.Serialization;
using FluentAssertions;
using Rackline.Application.Common;
using Rackline.Domain.Organisations;
using Rackline.Application.Rankings;
using Rackline.Application.Tournaments;
using Rackline.Domain.Matches;
using Rackline.Domain.Tournaments;
using Rackline.Web.Api;
using Rackline.Web.Tests.Persistence;

namespace Rackline.Web.Tests.Api;

[Trait("Category", "Integration")]
public class RankingApiTests(ApiFactory api) : IClassFixture<ApiFactory>
{
    private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web) { Converters = { new JsonStringEnumConverter() } };

    private static async Task<T> ReadAsync<T>(HttpResponseMessage response, HttpStatusCode expected = HttpStatusCode.OK)
    {
        response.StatusCode.Should().Be(expected, await response.Content.ReadAsStringAsync());
        return (await response.Content.ReadFromJsonAsync<T>(Json))!;
    }

    private static object NewRanking(Guid organisationId, string season, string discipline = "NineBall", int[]? points = null) => new
    {
        organisationId,
        name = $"Lowland {season} rankings",
        discipline,
        season,
        pointsByPosition = points ?? [60, 40, 25],
        participationPoints = 10,
    };

    /// <summary>A four-player 9-ball knockout counting towards the ranking, played out with the better seed winning.</summary>
    private async Task<(HttpClient Organiser, TournamentDetailDto Tournament, List<Guid> Players)> PlayedRankingTournamentAsync(Guid organisationId, List<Guid> players, Guid rankingId)
    {
        var organiser = await api.ClientForAsync(StaffRole.Organiser);
        var created = await ReadAsync<TournamentDetailDto>(await organiser.PostAsJsonAsync("/api/tournaments", new
        {
            organisationId,
            name = "Lowland Nine-Ball Masters",
            discipline = "NineBall",
            format = "SingleElimination",
            matchLengthKind = "RaceTo",
            matchLengthValue = 3,
            rankingId,
        }), HttpStatusCode.Created);
        created.RankingId.Should().Be(rankingId);
        created.RankingName.Should().StartWith("Lowland ").And.EndWith(" rankings");

        var id = created.Id;
        await ReadAsync<TournamentDetailDto>(await organiser.PostAsync($"/api/tournaments/{id}/open-registration", null));
        for (var i = 0; i < players.Count; i++)
        {
            await ReadAsync<TournamentDetailDto>(await organiser.PostAsJsonAsync($"/api/tournaments/{id}/entries", new { playerId = players[i], seed = i + 1 }), HttpStatusCode.Created);
        }

        await ReadAsync<DrawResponse>(await organiser.PostAsync($"/api/tournaments/{id}/draw?randomSeed=5", null));
        var tournament = await ReadAsync<TournamentDetailDto>(await organiser.PostAsync($"/api/tournaments/{id}/start", null));

        while (tournament.Matches.Where(m => m.State == MatchState.Ready).MinBy(m => m.Number) is { } match)
        {
            var seeds = tournament.Entries.ToDictionary(e => e.Id, e => e.Seed);
            var slot1Better = seeds[match.Slot1.EntryId!.Value] < seeds[match.Slot2.EntryId!.Value];
            api.Clock.Now = api.Clock.Now.AddMinutes(5);
            tournament = await ReadAsync<TournamentDetailDto>(await organiser.PostAsJsonAsync(
                $"/api/tournaments/{id}/matches/{match.Number}/result", new { score1 = slot1Better ? 3 : 2, score2 = slot1Better ? 2 : 3 }));
        }

        tournament = await ReadAsync<TournamentDetailDto>(await organiser.PostAsync($"/api/tournaments/{id}/complete", null));
        return (organiser, tournament, players);
    }

    [PostgresFact]
    public async Task A_ranking_tournament_awards_points_and_rates_every_result()
    {
        var (organisationId, _, players) = await api.AddReferenceDataAsync(4);
        var organiser = await api.ClientForAsync(StaffRole.Organiser);
        var created = await organiser.PostAsJsonAsync("/api/rankings", NewRanking(organisationId, "2026"));
        var ranking = await ReadAsync<RankingDetailDto>(created, HttpStatusCode.Created);
        created.Headers.Location!.ToString().Should().Be($"/api/rankings/{ranking.Id}");
        ranking.Table.Should().BeEmpty();

        var (_, tournament, _) = await PlayedRankingTournamentAsync(organisationId, players, ranking.Id);
        tournament.Status.Should().Be(TournamentStatus.Completed);

        // Anyone can read the results.
        var anonymous = api.CreateClient();
        ranking = await ReadAsync<RankingDetailDto>(await anonymous.GetAsync($"/api/rankings/{ranking.Id}"));
        // The two losing semi-finalists share third place.
        ranking.Table.Select(r => (r.Position, r.PlayerId, r.Points)).Should().BeEquivalentTo(new[]
        {
            (1, players[0], 60), (2, players[1], 40), (3, players[2], 25), (3, players[3], 25),
        });
        ranking.Table.Select(r => r.Position).Should().BeInAscendingOrder();
        ranking.Tournaments.Should().ContainSingle().Which.Should().Be(new RankedTournamentDto(tournament.Id, tournament.Name, TournamentStatus.Completed, true));

        var list = await ReadAsync<List<RankingSummaryDto>>(await anonymous.GetAsync("/api/rankings"));
        list.Should().ContainSingle(r => r.Id == ranking.Id).Which.TournamentsCounted.Should().Be(1);

        var standings = await ReadAsync<List<PlayerRankingDto>>(await anonymous.GetAsync($"/api/players/{players[1]}/rankings"));
        standings.Should().ContainSingle().Which.Should().BeEquivalentTo(new { RankingId = ranking.Id, Position = 2, Points = 40 }, o => o.ExcludingMissingMembers());

        // Three rated matches: the champion won two, the runner-up won one and lost one.
        var leaderboard = await ReadAsync<List<RatingRowDto>>(await anonymous.GetAsync("/api/ratings?discipline=NineBall"));
        leaderboard.Where(r => players.Contains(r.PlayerId)).Select(r => r.PlayerId).First().Should().Be(players[0]);
        leaderboard.Single(r => r.PlayerId == players[0]).Should().BeEquivalentTo(new { MatchesPlayed = 2, IsProvisional = true }, o => o.ExcludingMissingMembers());

        // The losing semi-finalists both lost one match from 1500, so they share a position.
        var semiFinalists = leaderboard.Where(r => r.PlayerId == players[2] || r.PlayerId == players[3]).ToList();
        semiFinalists.Select(r => r.Position).Distinct().Should().ContainSingle();

        var history = await ReadAsync<PlayerRatingsResponse>(await anonymous.GetAsync($"/api/players/{players[1]}/ratings"));
        history.Ratings.Should().ContainSingle().Which.MatchesPlayed.Should().Be(2);
        history.History.Should().HaveCount(2);
        history.History[0].Should().BeEquivalentTo(new { Won = false, OpponentId = players[0], TournamentId = tournament.Id, TournamentName = tournament.Name }, o => o.ExcludingMissingMembers());
        history.History[1].Won.Should().BeTrue();
        history.History[0].Before.Should().Be(history.History[1].After);
    }

    [PostgresFact]
    public async Task Rebuilding_ratings_is_for_admins_and_gives_the_same_answer()
    {
        var (organisationId, _, players) = await api.AddReferenceDataAsync(4);
        var organiser = await api.ClientForAsync(StaffRole.Organiser);
        var ranking = await ReadAsync<RankingDetailDto>(await organiser.PostAsJsonAsync("/api/rankings", NewRanking(organisationId, "2026")), HttpStatusCode.Created);
        await PlayedRankingTournamentAsync(organisationId, players, ranking.Id);
        var before = await ReadAsync<List<RatingRowDto>>(await organiser.GetAsync("/api/ratings?discipline=NineBall"));

        (await organiser.PostAsync("/api/ratings/rebuild", null)).StatusCode.Should().Be(HttpStatusCode.Forbidden);

        var admin = await api.ClientForAsync(Roles.Admin);
        var rebuilt = await ReadAsync<RebuildRatingsResponse>(await admin.PostAsync("/api/ratings/rebuild", null));

        rebuilt.ResultsRated.Should().BeGreaterThanOrEqualTo(3);
        var after = await ReadAsync<List<RatingRowDto>>(await admin.GetAsync("/api/ratings?discipline=NineBall"));
        after.Select(r => (r.PlayerId, Math.Round(r.Value, 9), r.MatchesPlayed))
            .Should().BeEquivalentTo(before.Select(r => (r.PlayerId, Math.Round(r.Value, 9), r.MatchesPlayed)));
    }

    [PostgresFact]
    public async Task Rankings_are_validated_and_need_an_organiser()
    {
        var (organisationId, _, _) = await api.AddReferenceDataAsync(0);
        var organiser = await api.ClientForAsync(StaffRole.Organiser);

        (await api.CreateClient().PostAsJsonAsync("/api/rankings", NewRanking(organisationId, "2027"))).StatusCode.Should().Be(HttpStatusCode.Unauthorized);
        (await (await api.ClientForAsync(StaffRole.Referee)).PostAsJsonAsync("/api/rankings", NewRanking(organisationId, "2027"))).StatusCode.Should().Be(HttpStatusCode.Forbidden);

        var rising = await organiser.PostAsJsonAsync("/api/rankings", NewRanking(organisationId, "2027", points: [10, 20]));
        rising.StatusCode.Should().Be(HttpStatusCode.BadRequest);
        (await rising.Content.ReadAsStringAsync()).Should().Contain("never be worth fewer points");

        (await organiser.PostAsJsonAsync("/api/rankings", NewRanking(organisationId, "2027"))).StatusCode.Should().Be(HttpStatusCode.Created);
        (await organiser.PostAsJsonAsync("/api/rankings", NewRanking(organisationId, "2027"))).StatusCode.Should().Be(HttpStatusCode.Conflict);

        (await organiser.PostAsJsonAsync("/api/rankings", NewRanking(Guid.CreateVersion7(), "2027"))).StatusCode.Should().Be(HttpStatusCode.NotFound);
        (await organiser.GetAsync($"/api/rankings/{Guid.CreateVersion7()}")).StatusCode.Should().Be(HttpStatusCode.NotFound);
        (await organiser.GetAsync($"/api/players/{Guid.CreateVersion7()}/ratings")).StatusCode.Should().Be(HttpStatusCode.NotFound);
    }

    [PostgresFact]
    public async Task A_tournament_cannot_count_towards_a_ranking_for_another_discipline()
    {
        var (organisationId, _, _) = await api.AddReferenceDataAsync(0);
        var organiser = await api.ClientForAsync(StaffRole.Organiser);
        var snooker = await ReadAsync<RankingDetailDto>(
            await organiser.PostAsJsonAsync("/api/rankings", NewRanking(organisationId, "2028", discipline: "Snooker")), HttpStatusCode.Created);

        var response = await organiser.PostAsJsonAsync("/api/tournaments", new
        {
            organisationId,
            name = "Wrong Discipline Open",
            discipline = "NineBall",
            format = "SingleElimination",
            matchLengthKind = "RaceTo",
            matchLengthValue = 3,
            rankingId = snooker.Id,
        });

        response.StatusCode.Should().Be(HttpStatusCode.Conflict);
        (await response.Content.ReadAsStringAsync()).Should().Contain("discipline");
    }

    [PostgresFact]
    public async Task The_public_pages_show_rankings_ratings_and_rating_changes()
    {
        var (organisationId, _, players) = await api.AddReferenceDataAsync(4);
        var organiser = await api.ClientForAsync(StaffRole.Organiser);
        var ranking = await ReadAsync<RankingDetailDto>(await organiser.PostAsJsonAsync("/api/rankings", NewRanking(organisationId, "2029")), HttpStatusCode.Created);
        var (_, tournament, _) = await PlayedRankingTournamentAsync(organisationId, players, ranking.Id);
        var visitor = api.CreateClient();

        async Task<string> PageAsync(string path)
        {
            var response = await visitor.GetAsync(path);
            response.StatusCode.Should().Be(HttpStatusCode.OK, path);
            var html = WebUtility.HtmlDecode(await response.Content.ReadAsStringAsync());
            html.Should().NotContain("\"type\":\"server\"", "public pages are static: no interactive Blazor circuit");
            return html;
        }

        (await PageAsync("/rankings")).Should().Contain("Lowland 2029 rankings").And.Contain("1 tournament counted");

        var rankingPage = await PageAsync($"/rankings/{ranking.Id}");
        rankingPage.Should().Contain("Testy McTest1").And.Contain("<strong>60</strong>");
        rankingPage.Should().Contain("1st: <strong>60</strong>").And.Contain("3rd: <strong>25</strong>").And.Contain("Below that: <strong>10</strong>");
        rankingPage.Should().Contain(tournament.Name).And.Contain("Points awarded");

        (await PageAsync($"/tournaments/{tournament.Id}")).Should().Contain("Counts towards").And.Contain($"/rankings/{ranking.Id}");

        var ratings = await PageAsync("/ratings?discipline=NineBall");
        ratings.Should().Contain("9-ball").And.Contain($"/players/{players[0]}").And.Contain("Provisional");

        var profile = await PageAsync($"/players/{players[1]}");
        profile.Should().Contain("Rating changes").And.Contain("Lowland 2029 rankings").And.Contain("2nd");
        profile.Should().Contain("+16").And.Contain(tournament.Name);

        (await visitor.GetAsync($"/rankings/{Guid.CreateVersion7()}")).StatusCode.Should().Be(HttpStatusCode.NotFound);
    }
}
