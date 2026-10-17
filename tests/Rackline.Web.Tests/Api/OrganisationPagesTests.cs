using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using System.Text.Json.Serialization;
using FluentAssertions;
using Rackline.Application.Common;
using Rackline.Application.Tournaments;
using Rackline.Domain.Organisations;
using Rackline.Web.Tests.Persistence;

namespace Rackline.Web.Tests.Api;

/// <summary>The organisation pages, and staff links that depend on where someone is appointed.</summary>
[Trait("Category", "Integration")]
public class OrganisationPagesTests(ApiFactory api) : IClassFixture<ApiFactory>
{
    private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web) { Converters = { new JsonStringEnumConverter() } };

    private static async Task<string> PageAsync(HttpClient client, string path)
    {
        var response = await client.GetAsync(path);
        response.StatusCode.Should().Be(HttpStatusCode.OK, path);
        return WebUtility.HtmlDecode(await response.Content.ReadAsStringAsync());
    }

    /// <summary>A started two-player tournament at the club.</summary>
    private async Task<TournamentDetailDto> StartedTournamentAsync(Guid club, List<Guid> players, string name)
    {
        var organiser = await api.ClientForAsync(StaffRole.Organiser);
        var created = await organiser.PostAsJsonAsync("/api/tournaments", new
        {
            organisationId = club, name, discipline = "NineBall", format = "SingleElimination", matchLengthKind = "RaceTo", matchLengthValue = 3,
        });
        var id = (await created.Content.ReadFromJsonAsync<TournamentDetailDto>(Json))!.Id;
        await organiser.PostAsync($"/api/tournaments/{id}/open-registration", null);
        foreach (var player in players)
        {
            await organiser.PostAsJsonAsync($"/api/tournaments/{id}/entries", new { playerId = player });
        }

        await organiser.PostAsync($"/api/tournaments/{id}/draw?randomSeed=1", null);
        return (await (await organiser.PostAsync($"/api/tournaments/{id}/start", null)).Content.ReadFromJsonAsync<TournamentDetailDto>(Json))!;
    }

    [PostgresFact]
    public async Task The_hierarchy_and_an_organisation_s_page_are_public_and_static()
    {
        var (club, _, players) = await api.AddReferenceDataAsync(2);
        var tournament = await StartedTournamentAsync(club, players, $"Club Night {Guid.NewGuid():N}"[..20]);
        var visitor = api.CreateClient();

        var list = await PageAsync(visitor, "/organisations");
        list.Should().Contain($"/organisations/{api.FederationId}").And.Contain($"/organisations/{club}");

        var page = await PageAsync(visitor, $"/organisations/{club}");
        page.Should().Contain("Test Hall").And.Contain("1 table").And.Contain(tournament.Name).And.Contain($"/organisations/{api.FederationId}");
        page.Should().NotContain("\"type\":\"server\"", "public pages have no interactive circuit");
        page.Should().NotContain("/staff", "visitors cannot manage staff");

        (await PageAsync(visitor, $"/tournaments/{tournament.Id}")).Should().Contain($"/organisations/{club}");
        (await visitor.GetAsync($"/organisations/{Guid.NewGuid()}")).StatusCode.Should().Be(HttpStatusCode.NotFound);
    }

    [PostgresFact]
    public async Task Staff_see_management_links_only_where_they_are_appointed()
    {
        var (club, _, players) = await api.AddReferenceDataAsync(2);
        var (otherClub, _, otherPlayers) = await api.AddReferenceDataAsync(2);
        var mine = await StartedTournamentAsync(club, players, $"Mine {Guid.NewGuid():N}"[..20]);
        var theirs = await StartedTournamentAsync(otherClub, otherPlayers, $"Theirs {Guid.NewGuid():N}"[..20]);

        var organiser = await api.BrowserSignedInAsync(await api.CreateUserAsync(StaffRole.Organiser, club));
        (await PageAsync(organiser, $"/organisations/{club}")).Should().Contain($"/organiser/organisations/{club}/staff");
        (await PageAsync(organiser, $"/organisations/{otherClub}")).Should().NotContain("/staff");

        var home = await PageAsync(organiser, "/organiser");
        home.Should().Contain($"/organisations/{club}").And.Contain("Organiser").And.NotContain($"/organisations/{otherClub}");

        // The scoring list shows only tournaments the user may score.
        var scoring = await PageAsync(organiser, "/referee");
        scoring.Should().Contain(mine.Name).And.NotContain(theirs.Name);
        (await PageAsync(organiser, $"/referee/tournaments/{theirs.Id}")).Should().Contain("cannot score these matches");
        (await PageAsync(organiser, $"/referee/tournaments/{mine.Id}")).Should().NotContain("cannot score these matches");

        // The staff page itself is interactive; its first render comes from the server.
        (await PageAsync(organiser, $"/organiser/organisations/{club}/staff")).Should().Contain("Staff");
    }

    [PostgresFact]
    public async Task People_without_an_appointment_cannot_reach_staff_pages()
    {
        var (club, _, _) = await api.AddReferenceDataAsync(0);
        var member = await api.BrowserSignedInAsync(await api.CreateUserAsync(null));

        var response = await member.GetAsync($"/organiser/organisations/{club}/staff");
        response.StatusCode.Should().Be(HttpStatusCode.Redirect);
        response.Headers.Location!.ToString().Should().Contain("/account/access-denied");

        var home = await PageAsync(member, "/organiser");
        home.Should().Contain("not been appointed");
        home.Should().NotContain(">Scoring<", "the scoring link is for staff");
    }
}
