using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using System.Text.Json.Serialization;
using FluentAssertions;
using Rackline.Application.Tournaments;
using Rackline.Domain.Organisations;
using Rackline.Web.Tests.Persistence;

namespace Rackline.Web.Tests.Api;

/// <summary>The transparent scoreboards for streaming software.</summary>
[Trait("Category", "Integration")]
public class OverlayTests(ApiFactory api) : IClassFixture<ApiFactory>
{
    private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web) { Converters = { new JsonStringEnumConverter() } };

    private readonly HttpClient _visitor = api.CreateClient();

    /// <summary>A started two-player knockout; its only match is ready to start.</summary>
    private async Task<(HttpClient Referee, TournamentDetailDto Tournament, Guid TableId)> ReadyMatchAsync()
    {
        var (club, tableId, players) = await api.AddReferenceDataAsync(2);
        var organiser = await api.ClientForAsync(StaffRole.Organiser);
        var created = await organiser.PostAsJsonAsync("/api/tournaments", new
        {
            organisationId = club, name = $"Stream Cup {Guid.NewGuid():N}"[..19], discipline = "NineBall", format = "SingleElimination",
            matchLengthKind = "RaceTo", matchLengthValue = 7,
        });
        var id = (await created.Content.ReadFromJsonAsync<TournamentDetailDto>(Json))!.Id;
        await organiser.PostAsync($"/api/tournaments/{id}/open-registration", null);
        foreach (var player in players)
        {
            await organiser.PostAsJsonAsync($"/api/tournaments/{id}/entries", new { playerId = player });
        }

        await organiser.PostAsync($"/api/tournaments/{id}/draw?randomSeed=1", null);
        var tournament = (await (await organiser.PostAsync($"/api/tournaments/{id}/start", null)).Content.ReadFromJsonAsync<TournamentDetailDto>(Json))!;
        return (organiser, tournament, tableId);
    }

    private async Task<string> PageAsync(string path)
    {
        var response = await _visitor.GetAsync(path);
        response.StatusCode.Should().Be(HttpStatusCode.OK, path);
        return WebUtility.HtmlDecode(await response.Content.ReadAsStringAsync());
    }

    [PostgresFact]
    public async Task A_match_overlay_is_a_bare_transparent_live_scoreboard()
    {
        var (referee, tournament, tableId) = await ReadyMatchAsync();
        var match = tournament.Matches.Single();
        await referee.PostAsync($"/api/tournaments/{tournament.Id}/matches/{match.Number}/start?tableId={tableId}", null);
        await referee.PostAsJsonAsync($"/api/tournaments/{tournament.Id}/matches/{match.Number}/frames", new { winnerSlot = 2 });

        var html = await PageAsync($"/overlay/matches/{match.Id}");

        html.Should().Contain("<html lang=\"en-GB\" class=\"overlay\">");
        html.Should().Contain("noindex").And.Contain("theme-dark").And.Contain($"data-live-match=\"{match.Id}\"");
        html.Should().Contain(tournament.Entries[0].PlayerName).And.Contain("Race to 7").And.Contain("Final");
        html.Should().NotContain("site-header", "an overlay has no site chrome");
        html.Should().NotContain("\"type\":\"server\"");

        (await PageAsync($"/overlay/matches/{match.Id}?theme=light")).Should().Contain("theme-light");
        (await PageAsync($"/matches/{match.Id}")).Should().Contain($"/overlay/matches/{match.Id}").And.NotContain("class=\"overlay\"");
        (await _visitor.GetAsync($"/overlay/matches/{Guid.NewGuid()}")).StatusCode.Should().Be(HttpStatusCode.NotFound);
    }

    [PostgresFact]
    public async Task A_table_overlay_follows_whatever_is_live_on_the_table()
    {
        var (referee, tournament, tableId) = await ReadyMatchAsync();
        var match = tournament.Matches.Single();

        var idle = await PageAsync($"/overlay/tables/{tableId}");
        idle.Should().Contain("http-equiv=\"refresh\"").And.NotContain("overlay-board", "nothing is being played yet");

        await referee.PostAsync($"/api/tournaments/{tournament.Id}/matches/{match.Number}/start?tableId={tableId}", null);
        var live = await PageAsync($"/overlay/tables/{tableId}");
        live.Should().Contain("overlay-board").And.Contain($"data-live-match=\"{match.Id}\"").And.NotContain("http-equiv=\"refresh\"");

        await referee.PostAsJsonAsync($"/api/tournaments/{tournament.Id}/matches/{match.Number}/result", new { score1 = 7, score2 = 2 });
        (await PageAsync($"/overlay/tables/{tableId}")).Should().NotContain("overlay-board", "the match has finished");

        (await _visitor.GetAsync($"/overlay/tables/{Guid.NewGuid()}")).StatusCode.Should().Be(HttpStatusCode.NotFound);
    }
}
