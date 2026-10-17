using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using System.Text.Json.Serialization;
using FluentAssertions;
using Rackline.Application.Common;
using Rackline.Application.Tournaments;
using Rackline.Domain.Matches;
using Rackline.Web.Tests.Persistence;

namespace Rackline.Web.Tests.Api;

/// <summary>The public, statically rendered pages, built from tournaments created through the API.</summary>
[Trait("Category", "Integration")]
public class PublicPagesTests(ApiFactory api) : IClassFixture<ApiFactory>
{
    private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web) { Converters = { new JsonStringEnumConverter() } };

    private readonly HttpClient _visitor = api.CreateClient();

    /// <summary>Creates a tournament for invented players; optionally opens, draws and starts it.</summary>
    private async Task<(HttpClient Organiser, TournamentDetailDto Tournament, List<Guid> Players)> TournamentAsync(
        string format, int players, bool start = true, string? name = null)
    {
        var (organisationId, _, playerIds) = await api.AddReferenceDataAsync(players);
        var organiser = await api.ClientForAsync(Roles.Organiser);
        name ??= $"Public {format} {Guid.NewGuid():N}"[..30];

        var created = await organiser.PostAsJsonAsync("/api/tournaments", new
        {
            organisationId, name, discipline = "TenBall", format, matchLengthKind = "RaceTo", matchLengthValue = 3,
        });
        var id = (await created.Content.ReadFromJsonAsync<TournamentDetailDto>(Json))!.Id;
        if (start)
        {
            await organiser.PostAsync($"/api/tournaments/{id}/open-registration", null);
            foreach (var player in playerIds)
            {
                await organiser.PostAsJsonAsync($"/api/tournaments/{id}/entries", new { playerId = player });
            }

            await organiser.PostAsync($"/api/tournaments/{id}/draw?randomSeed=3", null);
            await organiser.PostAsync($"/api/tournaments/{id}/start", null);
        }

        return (organiser, (await organiser.GetFromJsonAsync<TournamentDetailDto>($"/api/tournaments/{id}", Json))!, playerIds);
    }

    private async Task<TournamentDetailDto> ResultAsync(HttpClient client, Guid id, int match, int score1, int score2)
    {
        var response = await client.PostAsJsonAsync($"/api/tournaments/{id}/matches/{match}/result", new { score1, score2 });
        response.EnsureSuccessStatusCode();
        return (await response.Content.ReadFromJsonAsync<TournamentDetailDto>(Json))!;
    }

    private async Task<string> PageAsync(string path)
    {
        var response = await _visitor.GetAsync(path);
        response.StatusCode.Should().Be(HttpStatusCode.OK, path);
        var html = WebUtility.HtmlDecode(await response.Content.ReadAsStringAsync());
        html.Should().NotContain("\"type\":\"server\"", "public pages are static: no interactive Blazor circuit");
        return html;
    }

    [PostgresFact]
    public async Task The_tournament_list_shows_public_tournaments_by_status_and_hides_drafts()
    {
        var (_, live, _) = await TournamentAsync("SingleElimination", 2);
        var (_, draft, _) = await TournamentAsync("SingleElimination", 0, start: false);

        var html = await PageAsync("/tournaments");

        html.Should().Contain("In progress").And.Contain("Coming up").And.Contain("Finished");
        html.Should().Contain(live.Name).And.NotContain(draft.Name);
        (await _visitor.GetAsync($"/tournaments/{draft.Id}")).StatusCode.Should().Be(HttpStatusCode.NotFound);
    }

    [PostgresFact]
    public async Task A_knockout_shows_named_rounds_players_results_and_byes()
    {
        var (organiser, t, _) = await TournamentAsync("SingleElimination", 3);
        var first = t.Matches.Single(m => m.State == MatchState.Ready);
        await ResultAsync(organiser, t.Id, first.Number, 3, 1);

        var html = await PageAsync($"/tournaments/{t.Id}");

        html.Should().Contain("Semi-final").And.Contain("Final");
        html.Should().Contain("Testy McTest1").And.Contain("Testy McTest3");
        html.Should().Contain("Bye", "three players in a four-line draw means one bye");
        html.Should().Contain($"data-live-tournament=\"{t.Id}\"");
        html.Should().Contain($"data-match=\"{first.Id}\"").And.Contain("data-state=\"Completed\"");
    }

    [PostgresFact]
    public async Task A_double_elimination_shows_both_brackets_and_the_grand_final()
    {
        var (_, t, _) = await TournamentAsync("DoubleElimination", 4);

        var html = await PageAsync($"/tournaments/{t.Id}");

        html.Should().Contain("Winners' bracket").And.Contain("Losers' bracket").And.Contain("Grand final");
    }

    [PostgresFact]
    public async Task A_round_robin_shows_a_ranked_table()
    {
        var (organiser, t, _) = await TournamentAsync("RoundRobin", 3);
        var first = t.Matches.OrderBy(m => m.Number).First();
        var winnerSlot = first.Slot2;
        await ResultAsync(organiser, t.Id, first.Number, 1, 3);
        var winnerName = (await organiser.GetFromJsonAsync<TournamentDetailDto>($"/api/tournaments/{t.Id}", Json))!
            .Entries.Single(e => e.Id == winnerSlot.EntryId).PlayerName;

        var html = await PageAsync($"/tournaments/{t.Id}");

        html.Should().Contain("<table class=\"standings\">");
        var table = html[html.IndexOf("<tbody>", StringComparison.Ordinal)..html.IndexOf("</tbody>", StringComparison.Ordinal)];
        table.IndexOf(winnerName, StringComparison.Ordinal).Should().BeLessThan(
            table.IndexOf("Testy McTest", table.IndexOf(winnerName, StringComparison.Ordinal) + winnerName.Length, StringComparison.Ordinal),
            "the winner of the only result tops the table");
        table.Should().Contain("+2");
    }

    [PostgresFact]
    public async Task The_match_page_shows_the_score_and_listens_while_live()
    {
        var (organiser, t, _) = await TournamentAsync("SingleElimination", 2);
        var match = t.Matches.Single();
        await organiser.PostAsync($"/api/tournaments/{t.Id}/matches/{match.Number}/start", null);
        await organiser.PostAsJsonAsync($"/api/tournaments/{t.Id}/matches/{match.Number}/frames", new { winnerSlot = 2, breakAndRun = true });

        var live = await PageAsync($"/matches/{match.Id}");
        live.Should().Contain($"data-live-match=\"{match.Id}\"").And.Contain("Live since");
        live.Should().Contain("class=\"score js-score2\">1<").And.Contain("class=\"js-bnr2\">1<");

        await organiser.PostAsJsonAsync($"/api/tournaments/{t.Id}/matches/{match.Number}/frames", new { winnerSlot = 2 });
        await organiser.PostAsJsonAsync($"/api/tournaments/{t.Id}/matches/{match.Number}/frames", new { winnerSlot = 2 });
        var finished = await PageAsync($"/matches/{match.Id}");
        finished.Should().Contain("won 3–0").And.NotContain("data-live-match=\"");

        (await _visitor.GetAsync($"/matches/{Guid.NewGuid()}")).StatusCode.Should().Be(HttpStatusCode.NotFound);
    }

    [PostgresFact]
    public async Task Player_profiles_show_the_record_and_history_without_byes()
    {
        // Three players: whoever draws the bye then wins the final, so has one match played, not two.
        var (organiser, t, _) = await TournamentAsync("SingleElimination", 3);
        var byeEntry = t.Matches.Single(m => m.State == MatchState.Walkover).WinnerEntryId!.Value;
        var byePlayer = t.Entries.Single(e => e.Id == byeEntry);
        var semi = t.Matches.Single(m => m.State == MatchState.Ready);
        var afterSemi = await ResultAsync(organiser, t.Id, semi.Number, 3, 2);
        var final = afterSemi.Matches.Single(m => m.State == MatchState.Ready);
        var byePlayerIsSlot1 = final.Slot1.EntryId == byeEntry;
        await ResultAsync(organiser, t.Id, final.Number, byePlayerIsSlot1 ? 3 : 0, byePlayerIsSlot1 ? 0 : 3);

        var html = await PageAsync($"/players/{byePlayer.PlayerId}");

        html.Should().Contain(byePlayer.PlayerName);
        html.Should().Contain("<strong>1</strong><span>Played</span>", "the bye is not a match played");
        html.Should().Contain("<strong>1–0</strong><span>Won–lost</span>").And.Contain("<strong>3–0</strong><span>Frames</span>");
        html.Should().Contain(t.Name).And.Contain("Won 3–0");

        (await _visitor.GetAsync($"/players/{Guid.NewGuid()}")).StatusCode.Should().Be(HttpStatusCode.NotFound);
    }

    [PostgresFact]
    public async Task Player_profiles_hold_up_under_many_requests_at_once()
    {
        // Regression: the page once set the player before loading the rest. Under load the static
        // renderer drew it half-loaded at its first slow await, failing about one request in ten.
        var (organiser, tournament, players) = await TournamentAsync("SingleElimination", 2);
        await ResultAsync(organiser, tournament.Id, tournament.Matches.Single().Number, 3, 1);

        // Twenty at a time: enough to reproduce the old failure, without running the test database out of connections.
        using var gate = new SemaphoreSlim(20);
        var statuses = await Task.WhenAll(Enumerable.Range(0, 300).Select(async _ =>
        {
            await gate.WaitAsync();
            try
            {
                return (await _visitor.GetAsync($"/players/{players[0]}")).StatusCode;
            }
            finally
            {
                gate.Release();
            }
        }));

        statuses.Should().AllBeEquivalentTo(HttpStatusCode.OK);
    }

    [PostgresFact]
    public async Task Head_to_head_compares_two_players()
    {
        var (organiser, t, players) = await TournamentAsync("SingleElimination", 2);
        var match = t.Matches.Single();
        var firstIsSlot1 = match.Slot1.EntryId == t.Entries.Single(e => e.PlayerId == players[0]).Id;
        await ResultAsync(organiser, t.Id, match.Number, firstIsSlot1 ? 3 : 1, firstIsSlot1 ? 1 : 3);

        var html = await PageAsync($"/head-to-head?a={players[0]}&b={players[1]}");
        var reverse = await PageAsync($"/head-to-head?a={players[1]}&b={players[0]}");
        var nothingYet = await PageAsync($"/head-to-head?a={players[0]}&b={players[0]}");

        html.Should().Contain("Testy McTest1 v Testy McTest2").And.Contain("Frames 3–1").And.Contain("Won 3–1");
        reverse.Should().Contain("Frames 1–3").And.Contain("Lost 1–3");
        nothingYet.Should().Contain("Choose two different players.");
        (await _visitor.GetAsync($"/head-to-head?a={players[0]}&b={Guid.NewGuid()}")).StatusCode.Should().Be(HttpStatusCode.NotFound);
    }

    [PostgresFact]
    public async Task The_player_list_can_be_searched()
    {
        await api.AddReferenceDataAsync(2);

        var all = await PageAsync("/players");
        var none = await PageAsync("/players?search=zzzz-nobody");

        all.Should().Contain("Testy McTest1");
        none.Should().Contain("No players match");
    }
}
