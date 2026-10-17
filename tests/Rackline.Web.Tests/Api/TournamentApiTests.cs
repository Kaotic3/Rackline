using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using System.Text.Json.Serialization;
using FluentAssertions;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Rackline.Application.Common;
using Rackline.Application.Tournaments;
using Rackline.Domain.Matches;
using Rackline.Domain.Tournaments;
using Rackline.Web.Api;
using Rackline.Web.Tests.Persistence;

namespace Rackline.Web.Tests.Api;

[Trait("Category", "Integration")]
public class TournamentApiTests(ApiFactory api) : IClassFixture<ApiFactory>
{
    private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web) { Converters = { new JsonStringEnumConverter() } };

    private static object NewTournament(Guid organisationId, string format = "SingleElimination") => new
    {
        organisationId,
        name = "Lakeside Ten-Ball Open",
        discipline = "TenBall",
        format,
        matchLengthKind = "RaceTo",
        matchLengthValue = 5,
    };

    private static async Task<T> ReadAsync<T>(HttpResponseMessage response) =>
        (await response.Content.ReadFromJsonAsync<T>(Json))!;

    private static async Task<TournamentDetailDto> OkDetailAsync(HttpResponseMessage response)
    {
        response.StatusCode.Should().Be(HttpStatusCode.OK, await response.Content.ReadAsStringAsync());
        return await ReadAsync<TournamentDetailDto>(response);
    }

    private async Task<(HttpClient Organiser, TournamentDetailDto Tournament, List<Guid> Players)> RegisteredTournamentAsync(int players)
    {
        var (organisationId, _, playerIds) = await api.AddReferenceDataAsync(players);
        var organiser = await api.ClientForAsync(Roles.Organiser);

        var created = await organiser.PostAsJsonAsync("/api/tournaments", NewTournament(organisationId));
        var tournament = await ReadAsync<TournamentDetailDto>(created);
        await OkDetailAsync(await organiser.PostAsync($"/api/tournaments/{tournament.Id}/open-registration", null));

        foreach (var player in playerIds)
        {
            (await organiser.PostAsJsonAsync($"/api/tournaments/{tournament.Id}/entries", new { playerId = player }))
                .StatusCode.Should().Be(HttpStatusCode.Created);
        }

        return (organiser, tournament, playerIds);
    }

    [PostgresFact]
    public async Task An_organiser_and_a_referee_can_run_a_whole_tournament()
    {
        var (organisationId, tableId, players) = await api.AddReferenceDataAsync(5);
        var organiser = await api.ClientForAsync(Roles.Organiser);
        var referee = await api.ClientForAsync(Roles.Referee);

        var created = await organiser.PostAsJsonAsync("/api/tournaments", NewTournament(organisationId));
        created.StatusCode.Should().Be(HttpStatusCode.Created);
        var tournament = await ReadAsync<TournamentDetailDto>(created);
        created.Headers.Location!.ToString().Should().Be($"/api/tournaments/{tournament.Id}");
        tournament.Should().BeEquivalentTo(new { Status = TournamentStatus.Draft, MatchLength = new { FramesToWin = 5 } }, o => o.ExcludingMissingMembers());

        var id = tournament.Id;
        await OkDetailAsync(await organiser.PostAsync($"/api/tournaments/{id}/open-registration", null));
        foreach (var player in players)
        {
            await organiser.PostAsJsonAsync($"/api/tournaments/{id}/entries", new { playerId = player });
        }

        var entries = (await OkDetailAsync(await organiser.GetAsync($"/api/tournaments/{id}"))).Entries;
        entries.Should().HaveCount(5).And.OnlyContain(e => e.PlayerName.StartsWith("Testy McTest"));
        var top = entries.Single(e => e.PlayerId == players[0]);
        await OkDetailAsync(await organiser.PutAsJsonAsync($"/api/tournaments/{id}/entries/{top.Id}", new { seed = 1, handicap = 0 }));

        var drawResponse = await organiser.PostAsync($"/api/tournaments/{id}/draw?randomSeed=77", null);
        drawResponse.StatusCode.Should().Be(HttpStatusCode.OK);
        var draw = await ReadAsync<DrawResponse>(drawResponse);
        draw.RandomSeed.Should().Be(77);
        draw.Tournament.Status.Should().Be(TournamentStatus.Drawn);
        draw.Tournament.Matches.Should().HaveCount(7);
        draw.Tournament.Matches.Count(m => m.State == MatchState.Walkover).Should().Be(3, "five entries in an eight-line bracket means three byes");
        draw.Tournament.Matches.Single(m => m.Number == 1).Slot1.EntryId.Should().Be(top.Id, "seed 1 sits on the top line");

        await OkDetailAsync(await organiser.PostAsync($"/api/tournaments/{id}/start", null));

        // The referee plays every ready match on the one table, slot 1 winning 5–2.
        while ((await OkDetailAsync(await referee.GetAsync($"/api/tournaments/{id}"))).Matches
               .FirstOrDefault(m => m.State == MatchState.Ready) is { } match)
        {
            await OkDetailAsync(await referee.PostAsync($"/api/tournaments/{id}/matches/{match.Number}/start?tableId={tableId}", null));
            api.Clock.Now = api.Clock.Now.AddMinutes(10); // Small steps: bearer tokens expire after an hour.
            await OkDetailAsync(await referee.PostAsJsonAsync($"/api/tournaments/{id}/matches/{match.Number}/result", new { score1 = 5, score2 = 2 }));
        }

        var finished = await OkDetailAsync(await organiser.PostAsync($"/api/tournaments/{id}/complete", null));
        finished.Status.Should().Be(TournamentStatus.Completed);
        var final = finished.Matches.Single(m => m.WinnerTo is null);
        final.Should().BeEquivalentTo(new { State = MatchState.Completed, Score1 = 5, Score2 = 2, TableId = tableId }, o => o.ExcludingMissingMembers());
        final.CompletedAt.Should().Be(final.StartedAt!.Value.AddMinutes(10));
    }

    [PostgresFact]
    public async Task Reading_is_public_but_changes_need_the_right_role()
    {
        var (organisationId, _, _) = await api.AddReferenceDataAsync(0);
        var anonymous = api.CreateClient();
        var referee = await api.ClientForAsync(Roles.Referee);
        var player = await api.ClientForAsync(Roles.Player);
        var admin = await api.ClientForAsync(Roles.Admin);

        (await anonymous.GetAsync("/api/tournaments")).StatusCode.Should().Be(HttpStatusCode.OK);
        (await anonymous.GetAsync("/api/players")).StatusCode.Should().Be(HttpStatusCode.OK);

        (await anonymous.PostAsJsonAsync("/api/tournaments", NewTournament(organisationId))).StatusCode.Should().Be(HttpStatusCode.Unauthorized);
        (await referee.PostAsJsonAsync("/api/tournaments", NewTournament(organisationId))).StatusCode.Should().Be(HttpStatusCode.Forbidden);
        (await player.PostAsJsonAsync("/api/tournaments", NewTournament(organisationId))).StatusCode.Should().Be(HttpStatusCode.Forbidden);
        (await admin.PostAsJsonAsync("/api/tournaments", NewTournament(organisationId))).StatusCode.Should().Be(HttpStatusCode.Created);

        (await player.PostAsJsonAsync($"/api/tournaments/{Guid.NewGuid()}/matches/1/result", new { score1 = 5, score2 = 0 }))
            .StatusCode.Should().Be(HttpStatusCode.Forbidden);
        (await referee.PostAsJsonAsync($"/api/tournaments/{Guid.NewGuid()}/matches/1/result", new { score1 = 5, score2 = 0 }))
            .StatusCode.Should().Be(HttpStatusCode.NotFound, "referees may score, so they get past authorisation");
    }

    [PostgresFact]
    public async Task Api_errors_are_problem_json_while_pages_stay_html()
    {
        var anonymous = api.CreateClient();

        var unauthorised = await anonymous.PostAsJsonAsync("/api/tournaments", new { });
        var unknownRoute = await anonymous.GetAsync("/api/no-such-thing");
        var unknownPage = await anonymous.GetAsync("/no-such-page");

        unauthorised.StatusCode.Should().Be(HttpStatusCode.Unauthorized);
        unauthorised.Content.Headers.ContentType!.MediaType.Should().Be("application/problem+json");
        unknownRoute.StatusCode.Should().Be(HttpStatusCode.NotFound);
        unknownRoute.Content.Headers.ContentType!.MediaType.Should().Be("application/problem+json");
        unknownPage.StatusCode.Should().Be(HttpStatusCode.NotFound);
        unknownPage.Content.Headers.ContentType!.MediaType.Should().Be("text/html");
    }

    [PostgresFact]
    public async Task Unexpected_api_errors_are_problem_json_without_internal_details()
    {
        // Outside Development the error page path is set, which once made API crashes return HTML.
        var broken = api.WithWebHostBuilder(b => b.ConfigureServices(s =>
            s.Replace(ServiceDescriptor.Scoped<ITournamentQueries, ExplodingTournamentQueries>())));

        var response = await broken.CreateClient().GetAsync("/api/tournaments");

        response.StatusCode.Should().Be(HttpStatusCode.InternalServerError);
        response.Content.Headers.ContentType!.MediaType.Should().Be("application/problem+json");
        (await response.Content.ReadAsStringAsync()).Should().NotContain("Kaboom", "internal details must not leak");
    }

    private sealed class ExplodingTournamentQueries : ITournamentQueries
    {
        public Task<IReadOnlyList<TournamentSummaryDto>> ListAsync(TournamentStatus? status, CancellationToken cancellationToken) =>
            throw new InvalidOperationException("Kaboom: secret internal detail");

        public Task<TournamentDetailDto?> GetAsync(Guid id, CancellationToken cancellationToken) => throw new NotImplementedException();

        public Task<Guid?> FindTournamentIdForMatchAsync(Guid matchId, CancellationToken cancellationToken) => throw new NotImplementedException();
    }

    [PostgresFact]
    public async Task Listing_filters_by_status()
    {
        var (organiser, tournament, _) = await RegisteredTournamentAsync(2);

        var registration = await ReadAsync<List<TournamentSummaryDto>>(await organiser.GetAsync("/api/tournaments?status=Registration"));
        var completed = await ReadAsync<List<TournamentSummaryDto>>(await organiser.GetAsync("/api/tournaments?status=Completed"));

        registration.Should().ContainSingle(t => t.Id == tournament.Id).Which.EntryCount.Should().Be(2);
        completed.Should().NotContain(t => t.Id == tournament.Id);
    }

    [PostgresFact]
    public async Task Invalid_requests_are_rejected_with_validation_problems()
    {
        var (organisationId, _, _) = await api.AddReferenceDataAsync(0);
        var organiser = await api.ClientForAsync(Roles.Organiser);

        var noName = await organiser.PostAsJsonAsync("/api/tournaments", new
        {
            organisationId, name = "", discipline = "NineBall", format = "RoundRobin", matchLengthKind = "RaceTo", matchLengthValue = 5,
        });
        var evenBestOf = await organiser.PostAsJsonAsync("/api/tournaments", new
        {
            organisationId, name = "Even", discipline = "Snooker", format = "RoundRobin", matchLengthKind = "BestOf", matchLengthValue = 4,
        });
        var unknownFormat = await organiser.PostAsJsonAsync("/api/tournaments", new
        {
            organisationId, name = "Odd", discipline = "Snooker", format = "Knockabout", matchLengthKind = "BestOf", matchLengthValue = 5,
        });

        noName.StatusCode.Should().Be(HttpStatusCode.BadRequest);
        (await ReadAsync<ValidationProblemDetails>(noName)).Errors.Should().ContainKey("Name");
        evenBestOf.StatusCode.Should().Be(HttpStatusCode.BadRequest);
        (await evenBestOf.Content.ReadAsStringAsync()).Should().Contain("odd number of frames");
        unknownFormat.StatusCode.Should().Be(HttpStatusCode.BadRequest);
    }

    [PostgresFact]
    public async Task Missing_things_are_not_found()
    {
        var organiser = await api.ClientForAsync(Roles.Organiser);
        var (_, tournament, _) = await RegisteredTournamentAsync(0);

        var noTournament = await organiser.GetAsync($"/api/tournaments/{Guid.NewGuid()}");
        var noOrganisation = await organiser.PostAsJsonAsync("/api/tournaments", NewTournament(Guid.NewGuid()));
        var noPlayer = await organiser.PostAsJsonAsync($"/api/tournaments/{tournament.Id}/entries", new { playerId = Guid.NewGuid() });

        noTournament.StatusCode.Should().Be(HttpStatusCode.NotFound);
        noTournament.Content.Headers.ContentType!.MediaType.Should().Be("application/problem+json");
        noOrganisation.StatusCode.Should().Be(HttpStatusCode.NotFound);
        (await ReadAsync<ProblemDetails>(noPlayer)).Detail.Should().StartWith("Player");
    }

    [PostgresFact]
    public async Task Breaking_a_domain_rule_is_a_conflict()
    {
        var (organiser, tournament, players) = await RegisteredTournamentAsync(2);

        var startTooEarly = await organiser.PostAsync($"/api/tournaments/{tournament.Id}/start", null);
        var duplicateEntry = await organiser.PostAsJsonAsync($"/api/tournaments/{tournament.Id}/entries", new { playerId = players[0] });

        startTooEarly.StatusCode.Should().Be(HttpStatusCode.Conflict);
        (await ReadAsync<ProblemDetails>(startTooEarly)).Title.Should().Be("Not allowed");
        duplicateEntry.StatusCode.Should().Be(HttpStatusCode.Conflict);
    }

    [PostgresFact]
    public async Task Updating_withdrawing_and_undoing_the_draw()
    {
        var (organiser, tournament, _) = await RegisteredTournamentAsync(4);
        var id = tournament.Id;

        var updated = await OkDetailAsync(await organiser.PutAsJsonAsync($"/api/tournaments/{id}", new
        {
            name = "Renamed Open", discipline = "Snooker", format = "DoubleElimination", matchLengthKind = "BestOf", matchLengthValue = 5, grandFinalReset = false,
        }));
        updated.Should().BeEquivalentTo(new { Name = "Renamed Open", Format = TournamentFormat.DoubleElimination, GrandFinalReset = false }, o => o.ExcludingMissingMembers());

        var afterWithdrawal = await OkDetailAsync(await organiser.DeleteAsync($"/api/tournaments/{id}/entries/{updated.Entries[0].Id}"));
        afterWithdrawal.Entries.Should().HaveCount(3);

        await organiser.PostAsync($"/api/tournaments/{id}/draw", null);
        var undone = await OkDetailAsync(await organiser.DeleteAsync($"/api/tournaments/{id}/draw"));
        undone.Should().BeEquivalentTo(new { Status = TournamentStatus.Registration, Matches = Array.Empty<MatchDto>() }, o => o.ExcludingMissingMembers());
    }

    [PostgresFact]
    public async Task Walkovers_are_recorded()
    {
        var (organiser, tournament, _) = await RegisteredTournamentAsync(2);
        await organiser.PostAsync($"/api/tournaments/{tournament.Id}/draw?randomSeed=1", null);
        var started = await OkDetailAsync(await organiser.PostAsync($"/api/tournaments/{tournament.Id}/start", null));
        var final = started.Matches.Single();

        var result = await OkDetailAsync(await organiser.PostAsJsonAsync(
            $"/api/tournaments/{tournament.Id}/matches/{final.Number}/walkover", new { winnerEntryId = final.Slot2.EntryId }));

        result.Matches.Single().Should().BeEquivalentTo(new { State = MatchState.Walkover, WinnerEntryId = final.Slot2.EntryId }, o => o.ExcludingMissingMembers());
    }

    [PostgresFact]
    public async Task Only_tournaments_without_a_live_draw_can_be_deleted()
    {
        var (organiser, tournament, _) = await RegisteredTournamentAsync(2);
        await organiser.PostAsync($"/api/tournaments/{tournament.Id}/draw", null);

        (await organiser.DeleteAsync($"/api/tournaments/{tournament.Id}")).StatusCode.Should().Be(HttpStatusCode.Conflict);

        await organiser.PostAsync($"/api/tournaments/{tournament.Id}/cancel", null);
        (await organiser.DeleteAsync($"/api/tournaments/{tournament.Id}")).StatusCode.Should().Be(HttpStatusCode.NoContent);
        (await organiser.GetAsync($"/api/tournaments/{tournament.Id}")).StatusCode.Should().Be(HttpStatusCode.NotFound);
    }
}
