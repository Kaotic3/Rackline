using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using System.Text.Json.Serialization;
using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Rackline.Application.Challenges;
using Rackline.Domain.Common;
using Rackline.Domain.Organisations;
using Rackline.Domain.Rankings;
using Rackline.Web.Tests.Persistence;

namespace Rackline.Web.Tests.Api;

[Trait("Category", "Integration")]
public class ChallengeApiTests(ApiFactory api) : IClassFixture<ApiFactory>
{
    private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web) { Converters = { new JsonStringEnumConverter() } };

    private static async Task<T> ReadAsync<T>(HttpResponseMessage response, HttpStatusCode expected = HttpStatusCode.OK)
    {
        response.StatusCode.Should().Be(expected, await response.Content.ReadAsStringAsync());
        return (await response.Content.ReadFromJsonAsync<T>(Json))!;
    }

    /// <summary>A club with two members, both given settled 9-ball ratings close together.</summary>
    private async Task<(Guid Club, Guid Ann, Guid Ben)> ClubWithSettledPlayersAsync()
    {
        var (club, _, players) = await api.AddReferenceDataAsync(2);
        await using var db = api.CreateDbContext();
        foreach (var (player, wins) in new[] { (players[0], 10), (players[1], 11) })
        {
            var history = Enumerable.Range(0, Elo.ProvisionalMatches).Select(i =>
            {
                var opponent = Guid.CreateVersion7();
                var at = api.Clock.Now.AddDays(-100 + i);
                return i < wins
                    ? new RatedResult(Guid.CreateVersion7(), Discipline.NineBall, player, opponent, at)
                    : new RatedResult(Guid.CreateVersion7(), Discipline.NineBall, opponent, player, at);
            });
            db.Ratings.Add(Elo.Replay(history).Ratings.Single(r => r.PlayerId == player));
        }

        foreach (var member in await db.Players.Where(p => players.Contains(p.Id)).ToListAsync())
        {
            member.JoinOrganisation(club);
        }

        await db.SaveChangesAsync();
        return (club, players[0], players[1]);
    }

    private static object Challenge(Guid club, Guid player1, Guid player2, int score1 = 5, int score2 = 3) => new
    {
        organisationId = club,
        player1Id = player1,
        player2Id = player2,
        discipline = "NineBall",
        matchLengthKind = "RaceTo",
        matchLengthValue = 5,
        score1,
        score2,
    };

    [PostgresFact]
    public async Task A_club_referee_records_challenges_and_only_fair_ones_count()
    {
        var (club, ann, ben) = await ClubWithSettledPlayersAsync();
        var referee = await api.ClientForAsync(StaffRole.Referee, club);
        var visitor = api.CreateClient();

        var preview = await ReadAsync<ChallengePreviewDto>(await visitor.GetAsync($"/api/challenges/preview?player1={ann}&player2={ben}&discipline=NineBall"));
        preview.Counts.Should().BeTrue();

        var created = await referee.PostAsJsonAsync("/api/challenges", Challenge(club, ann, ben));
        var first = await ReadAsync<ChallengeDto>(created, HttpStatusCode.Created);
        created.Headers.Location!.ToString().Should().Be($"/api/challenges/{first.Id}");
        first.Should().BeEquivalentTo(new { Rated = true, WinnerId = ann, Score1 = 5, Score2 = 3 }, o => o.ExcludingMissingMembers());
        first.RatingChange1.Should().BePositive();
        first.RatingChange2.Should().BeNegative();

        // A rematch a week later is recorded, but does not count.
        api.Clock.Now = api.Clock.Now.AddDays(7);
        referee = await api.ClientForAsync(StaffRole.Referee, club); // the old token has expired on the test clock
        (await ReadAsync<ChallengePreviewDto>(await visitor.GetAsync($"/api/challenges/preview?player1={ben}&player2={ann}&discipline=NineBall")))
            .Reasons.Should().ContainSingle().Which.Should().Contain("30 days");
        var rematch = await ReadAsync<ChallengeDto>(await referee.PostAsJsonAsync("/api/challenges", Challenge(club, ben, ann)), HttpStatusCode.Created);
        rematch.Rated.Should().BeFalse();
        rematch.RatingChange1.Should().BeNull();

        var annsChallenges = await ReadAsync<List<ChallengeDto>>(await visitor.GetAsync($"/api/challenges?playerId={ann}"));
        annsChallenges.Select(c => c.Id).Should().Equal(rematch.Id, first.Id);

        // The rated one shows in the rating history, as a match with no tournament.
        var history = await ReadAsync<RatingsResponse>(await visitor.GetAsync($"/api/players/{ann}/ratings"));
        history.History.Should().ContainSingle(h => h.MatchId == first.Id && h.TournamentId == null);
    }

    [PostgresFact]
    public async Task Challenges_need_staff_and_members_and_only_organisers_remove_them()
    {
        var (club, ann, ben) = await ClubWithSettledPlayersAsync();
        var (otherClub, _, outsiders) = await api.AddReferenceDataAsync(1);

        (await api.CreateClient().PostAsJsonAsync("/api/challenges", Challenge(club, ann, ben))).StatusCode.Should().Be(HttpStatusCode.Unauthorized);
        var stranger = await api.ClientForAsync(StaffRole.Referee, otherClub);
        (await stranger.PostAsJsonAsync("/api/challenges", Challenge(club, ann, ben))).StatusCode.Should().Be(HttpStatusCode.Forbidden);

        var referee = await api.ClientForAsync(StaffRole.Referee, club);
        var notMember = await referee.PostAsJsonAsync("/api/challenges", Challenge(club, ann, outsiders[0]));
        notMember.StatusCode.Should().Be(HttpStatusCode.Conflict);
        (await notMember.Content.ReadAsStringAsync()).Should().Contain("is not a member");
        (await referee.PostAsJsonAsync("/api/challenges", Challenge(club, ann, ben, 5, 5))).StatusCode.Should().Be(HttpStatusCode.Conflict);

        var recorded = await ReadAsync<ChallengeDto>(await referee.PostAsJsonAsync("/api/challenges", Challenge(club, ann, ben)), HttpStatusCode.Created);
        (await referee.DeleteAsync($"/api/challenges/{recorded.Id}")).StatusCode.Should().Be(HttpStatusCode.Forbidden);

        var organiser = await api.ClientForAsync(StaffRole.Organiser, club);
        (await organiser.DeleteAsync($"/api/challenges/{recorded.Id}")).StatusCode.Should().Be(HttpStatusCode.NoContent);
        (await organiser.GetAsync($"/api/challenges/{recorded.Id}")).StatusCode.Should().Be(HttpStatusCode.NotFound);

        await using var db = api.CreateDbContext();
        (await db.RatingChanges.AnyAsync(c => c.MatchId == recorded.Id)).Should().BeFalse("removing a rated challenge rebuilds every rating without it");
    }

    [PostgresFact]
    public async Task Challenges_have_public_pages_and_appear_in_records_and_head_to_heads()
    {
        var (club, ann, ben) = await ClubWithSettledPlayersAsync();
        var (_, _, newcomers) = await api.AddReferenceDataAsync(1);
        await using (var db = api.CreateDbContext())
        {
            (await db.Players.SingleAsync(p => p.Id == newcomers[0])).JoinOrganisation(club);
            await db.SaveChangesAsync();
        }

        var referee = await api.ClientForAsync(StaffRole.Referee, club);
        var rated = await ReadAsync<ChallengeDto>(await referee.PostAsJsonAsync("/api/challenges", Challenge(club, ann, ben, 5, 2)), HttpStatusCode.Created);
        var unrated = await ReadAsync<ChallengeDto>(await referee.PostAsJsonAsync("/api/challenges", Challenge(club, newcomers[0], ann, 5, 4)), HttpStatusCode.Created);
        var visitor = api.CreateClient();

        async Task<string> PageAsync(string path)
        {
            var response = await visitor.GetAsync(path);
            response.StatusCode.Should().Be(HttpStatusCode.OK, path);
            var html = WebUtility.HtmlDecode(await response.Content.ReadAsStringAsync());
            html.Should().NotContain("\"type\":\"server\"", "public pages are static");
            return html;
        }

        (await PageAsync("/challenges")).Should().Contain($"/challenges/{rated.Id}").And.Contain("within 200 points").And.Contain("Not rated");
        (await PageAsync($"/challenges/{rated.Id}")).Should().Contain("counted towards both players' ratings").And.Contain("Rating +");
        (await PageAsync($"/challenges/{unrated.Id}")).Should().Contain("did not count towards ratings").And.Contain("settled rating");

        var profile = await PageAsync($"/players/{ann}");
        profile.Should().Contain($"/challenges/{rated.Id}").And.Contain("Won 5–2").And.Contain("Lost 4–5").And.Contain(", challenge");
        (await PageAsync($"/head-to-head?a={ann}&b={ben}")).Should().Contain("Won 5–2");
        (await visitor.GetAsync($"/challenges/{Guid.NewGuid()}")).StatusCode.Should().Be(HttpStatusCode.NotFound);
    }

    [PostgresFact]
    public async Task The_recording_screen_is_for_staff()
    {
        var (club, _, _) = await ClubWithSettledPlayersAsync();
        var browser = api.CreateClient(new Microsoft.AspNetCore.Mvc.Testing.WebApplicationFactoryClientOptions { AllowAutoRedirect = false });
        (await browser.GetAsync("/referee/challenges/new")).StatusCode.Should().Be(HttpStatusCode.Redirect);

        var referee = await api.BrowserSignedInAsync(await api.CreateUserAsync(StaffRole.Referee, club));
        (await referee.GetStringAsync("/referee")).Should().Contain("/referee/challenges/new");
        (await referee.GetAsync("/referee/challenges/new")).StatusCode.Should().Be(HttpStatusCode.OK);
    }

    private sealed record HistoryItem(Guid MatchId, Guid? TournamentId);

    private sealed record RatingsResponse(List<HistoryItem> History);
}
