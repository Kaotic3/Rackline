using System.Collections.Concurrent;
using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using System.Text.Json.Serialization;
using FluentAssertions;
using Microsoft.AspNetCore.Http.Connections;
using Microsoft.AspNetCore.SignalR.Client;
using Microsoft.Extensions.DependencyInjection;
using Rackline.Application.Common;
using Rackline.Application.Live;
using Rackline.Application.Tournaments;
using Rackline.Domain.Matches;
using Rackline.Domain.Tournaments;
using Rackline.Web.Tests.Persistence;

namespace Rackline.Web.Tests.Api;

[Trait("Category", "Integration")]
public class LiveScoringApiTests(ApiFactory api) : IClassFixture<ApiFactory>
{
    private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web) { Converters = { new JsonStringEnumConverter() } };

    private static readonly TimeSpan Wait = TimeSpan.FromSeconds(10);

    /// <summary>A race-to-3 knockout of four, drawn and started, with match 1 live.</summary>
    private async Task<(HttpClient Referee, TournamentDetailDto Tournament)> LiveMatchAsync()
    {
        var (organisationId, _, players) = await api.AddReferenceDataAsync(4);
        var organiser = await api.ClientForAsync(Roles.Organiser);
        var referee = await api.ClientForAsync(Roles.Referee);

        var created = await organiser.PostAsJsonAsync("/api/tournaments", new
        {
            organisationId, name = "Live Wire Cup", discipline = "NineBall", format = "SingleElimination", matchLengthKind = "RaceTo", matchLengthValue = 3,
        });
        var id = (await created.Content.ReadFromJsonAsync<TournamentDetailDto>(Json))!.Id;
        await organiser.PostAsync($"/api/tournaments/{id}/open-registration", null);
        foreach (var player in players)
        {
            await organiser.PostAsJsonAsync($"/api/tournaments/{id}/entries", new { playerId = player });
        }

        await organiser.PostAsync($"/api/tournaments/{id}/draw?randomSeed=4", null);
        await organiser.PostAsync($"/api/tournaments/{id}/start", null);
        var started = await referee.PostAsync($"/api/tournaments/{id}/matches/1/start", null);
        started.EnsureSuccessStatusCode();
        return (referee, (await started.Content.ReadFromJsonAsync<TournamentDetailDto>(Json))!);
    }

    private async Task<HubConnection> ConnectAsync()
    {
        var connection = new HubConnectionBuilder()
            .WithUrl(new Uri(api.Server.BaseAddress, "/hubs/live"), o =>
            {
                o.HttpMessageHandlerFactory = _ => api.Server.CreateHandler();
                o.Transports = HttpTransportType.LongPolling;
            })
            .AddJsonProtocol(o => o.PayloadSerializerOptions.Converters.Add(new JsonStringEnumConverter()))
            .Build();
        await connection.StartAsync();
        return connection;
    }

    private static async Task<TournamentDetailDto> FrameAsync(HttpClient client, Guid id, int match, int winnerSlot, bool breakAndRun = false)
    {
        var response = await client.PostAsJsonAsync($"/api/tournaments/{id}/matches/{match}/frames", new { winnerSlot, breakAndRun });
        response.StatusCode.Should().Be(HttpStatusCode.OK, await response.Content.ReadAsStringAsync());
        return (await response.Content.ReadFromJsonAsync<TournamentDetailDto>(Json))!;
    }

    [PostgresFact]
    public async Task Frames_build_the_score_and_the_deciding_frame_moves_the_winner_on()
    {
        var (referee, tournament) = await LiveMatchAsync();
        var id = tournament.Id;

        await FrameAsync(referee, id, 1, 1, breakAndRun: true);
        await FrameAsync(referee, id, 1, 2);
        var mid = await FrameAsync(referee, id, 1, 1);
        mid.Matches.Single(m => m.Number == 1).Should().BeEquivalentTo(
            new { Score1 = 2, Score2 = 1, State = MatchState.Live, BreakAndRuns1 = 1, BreakAndRuns2 = 0 }, o => o.ExcludingMissingMembers());

        var done = await FrameAsync(referee, id, 1, 1);
        var opener = done.Matches.Single(m => m.Number == 1);
        opener.Should().BeEquivalentTo(new { Score1 = 3, Score2 = 1, State = MatchState.Completed }, o => o.ExcludingMissingMembers());
        done.Matches.Single(m => m.Number == 3).Slot1.EntryId.Should().Be(opener.Slot1.EntryId);
    }

    [PostgresFact]
    public async Task The_last_frame_can_be_taken_back()
    {
        var (referee, tournament) = await LiveMatchAsync();
        await FrameAsync(referee, tournament.Id, 1, 2);

        var undone = await referee.DeleteAsync($"/api/tournaments/{tournament.Id}/matches/1/frames/last");
        var again = await referee.DeleteAsync($"/api/tournaments/{tournament.Id}/matches/1/frames/last");

        undone.StatusCode.Should().Be(HttpStatusCode.OK);
        (await undone.Content.ReadFromJsonAsync<TournamentDetailDto>(Json))!.Matches.Single(m => m.Number == 1).Score2.Should().Be(0);
        again.StatusCode.Should().Be(HttpStatusCode.Conflict, "there is nothing left to take back");
    }

    [PostgresFact]
    public async Task Frame_requests_are_validated_and_need_a_scorer()
    {
        var (referee, tournament) = await LiveMatchAsync();
        var player = await api.ClientForAsync(Roles.Player);

        (await referee.PostAsJsonAsync($"/api/tournaments/{tournament.Id}/matches/1/frames", new { winnerSlot = 3 }))
            .StatusCode.Should().Be(HttpStatusCode.BadRequest);
        (await referee.PostAsJsonAsync($"/api/tournaments/{tournament.Id}/matches/1/frames", new { }))
            .StatusCode.Should().Be(HttpStatusCode.BadRequest);
        (await player.PostAsJsonAsync($"/api/tournaments/{tournament.Id}/matches/1/frames", new { winnerSlot = 1 }))
            .StatusCode.Should().Be(HttpStatusCode.Forbidden);
        (await referee.PostAsJsonAsync($"/api/tournaments/{tournament.Id}/matches/2/frames", new { winnerSlot = 1 }))
            .StatusCode.Should().Be(HttpStatusCode.Conflict, "match 2 has not started");
    }

    [PostgresFact]
    public async Task Spectators_watching_a_match_or_tournament_receive_live_updates()
    {
        var (referee, tournament) = await LiveMatchAsync();
        var opener = tournament.Matches.Single(m => m.Number == 1);

        var matchUpdates = new BlockingCollection<MatchDto>();
        var tournamentUpdates = new BlockingCollection<(Guid TournamentId, MatchDto[] Matches)>();
        await using var matchWatcher = await ConnectAsync();
        await using var tournamentWatcher = await ConnectAsync();
        matchWatcher.On<MatchDto>(LiveChannels.MatchUpdated, matchUpdates.Add);
        tournamentWatcher.On<Guid, MatchDto[]>(LiveChannels.MatchesUpdated, (tid, matches) => tournamentUpdates.Add((tid, matches)));
        await matchWatcher.InvokeAsync("WatchMatch", opener.Id);
        await tournamentWatcher.InvokeAsync("WatchTournament", tournament.Id);

        await FrameAsync(referee, tournament.Id, 1, 2, breakAndRun: true);

        matchUpdates.TryTake(out var update, Wait).Should().BeTrue("the match watcher should hear about the frame");
        update!.Should().BeEquivalentTo(new { Number = 1, Score1 = 0, Score2 = 1, BreakAndRuns2 = 1, State = MatchState.Live }, o => o.ExcludingMissingMembers());

        tournamentUpdates.TryTake(out var batch, Wait).Should().BeTrue("the tournament watcher should hear about the frame");
        batch.TournamentId.Should().Be(tournament.Id);
        batch.Matches.Select(m => m.Number).Should().Equal(1);

        // The deciding frames: the tournament watcher also hears about the match the winner moves into.
        await FrameAsync(referee, tournament.Id, 1, 2);
        await FrameAsync(referee, tournament.Id, 1, 2);
        tournamentUpdates.TryTake(out _, Wait).Should().BeTrue();
        tournamentUpdates.TryTake(out var deciding, Wait).Should().BeTrue();
        deciding.Matches.Select(m => m.Number).Should().Equal(1, 3);
    }

    [PostgresFact]
    public async Task Watchers_only_hear_about_what_they_watch_and_can_stop()
    {
        var (referee, tournament) = await LiveMatchAsync();
        await referee.PostAsync($"/api/tournaments/{tournament.Id}/matches/2/start", null);
        var opener = tournament.Matches.Single(m => m.Number == 1);

        var updates = new BlockingCollection<MatchDto>();
        await using var watcher = await ConnectAsync();
        watcher.On<MatchDto>(LiveChannels.MatchUpdated, updates.Add);
        await watcher.InvokeAsync("WatchMatch", opener.Id);

        await FrameAsync(referee, tournament.Id, 2, 1);
        await FrameAsync(referee, tournament.Id, 1, 1);
        updates.TryTake(out var first, Wait).Should().BeTrue();
        first!.Number.Should().Be(1, "updates for match 2 are not sent to match 1's watchers");

        await watcher.InvokeAsync("UnwatchMatch", opener.Id);
        await FrameAsync(referee, tournament.Id, 1, 1);
        updates.TryTake(out _, TimeSpan.FromSeconds(1)).Should().BeFalse();
    }

    [PostgresFact]
    public async Task Status_changes_are_announced_to_the_tournament()
    {
        var (organisationId, _, players) = await api.AddReferenceDataAsync(2);
        var organiser = await api.ClientForAsync(Roles.Organiser);
        var created = await organiser.PostAsJsonAsync("/api/tournaments", new
        {
            organisationId, name = "Status Cup", discipline = "TenBall", format = "RoundRobin", matchLengthKind = "RaceTo", matchLengthValue = 2,
        });
        var id = (await created.Content.ReadFromJsonAsync<TournamentDetailDto>(Json))!.Id;

        var statuses = new BlockingCollection<TournamentStatus>();
        await using var watcher = await ConnectAsync();
        watcher.On<Guid, TournamentStatus>(LiveChannels.TournamentUpdated, (_, status) => statuses.Add(status));
        await watcher.InvokeAsync("WatchTournament", id);

        await organiser.PostAsync($"/api/tournaments/{id}/open-registration", null);
        await organiser.PostAsJsonAsync($"/api/tournaments/{id}/entries", new { playerId = players[0] });
        await organiser.PostAsJsonAsync($"/api/tournaments/{id}/entries", new { playerId = players[1] });
        await organiser.PostAsync($"/api/tournaments/{id}/draw", null);

        var received = new List<TournamentStatus>();
        while (received.Count < 4 && statuses.TryTake(out var status, Wait))
        {
            received.Add(status);
        }

        received.Should().Equal(TournamentStatus.Registration, TournamentStatus.Registration, TournamentStatus.Registration, TournamentStatus.Drawn);
    }

    [PostgresFact]
    public async Task Server_side_pages_get_the_same_updates_in_process()
    {
        var (referee, tournament) = await LiveMatchAsync();
        var feed = api.Services.GetRequiredService<LiveFeed>();
        var heard = new BlockingCollection<LiveUpdate>();
        void Listener(LiveUpdate u)
        {
            if (u.TournamentId == tournament.Id)
            {
                heard.Add(u);
            }
        }

        feed.Published += Listener;
        try
        {
            await FrameAsync(referee, tournament.Id, 1, 1);
            heard.TryTake(out var update, Wait).Should().BeTrue();
            update!.Matches.Single().Score1.Should().Be(1);
        }
        finally
        {
            feed.Published -= Listener;
        }
    }
}
