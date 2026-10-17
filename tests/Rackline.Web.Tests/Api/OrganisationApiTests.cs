using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using System.Text.Json.Serialization;
using FluentAssertions;
using Rackline.Application.Common;
using Rackline.Application.Organisations;
using Rackline.Application.Tournaments;
using Rackline.Domain.Organisations;
using Rackline.Domain.Tournaments;
using Rackline.Web.Api;
using Rackline.Web.Tests.Persistence;

namespace Rackline.Web.Tests.Api;

[Trait("Category", "Integration")]
public class OrganisationApiTests(ApiFactory api) : IClassFixture<ApiFactory>
{
    private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web) { Converters = { new JsonStringEnumConverter() } };

    private static async Task<T> ReadAsync<T>(HttpResponseMessage response, HttpStatusCode expected = HttpStatusCode.OK)
    {
        response.StatusCode.Should().Be(expected, await response.Content.ReadAsStringAsync());
        return (await response.Content.ReadFromJsonAsync<T>(Json))!;
    }

    private static async Task<Guid> CreateOrganisationAsync(HttpClient client, string name, string level, Guid? parentId) =>
        (await ReadAsync<OrganisationDetailDto>(await client.PostAsJsonAsync("/api/organisations", new { name, level, parentId }), HttpStatusCode.Created)).Id;

    private static object Tournament(Guid organisationId) => new
    {
        organisationId,
        name = "Hierarchy Open",
        discipline = "NineBall",
        format = "SingleElimination",
        matchLengthKind = "RaceTo",
        matchLengthValue = 3,
    };

    [PostgresFact]
    public async Task Organisers_act_within_their_part_of_the_hierarchy_and_appointments_count_at_once()
    {
        var admin = await api.ClientForAsync(Roles.Admin);
        var nation = await CreateOrganisationAsync(admin, $"Nation {Guid.NewGuid():N}"[..20], "National", null);
        var east = await CreateOrganisationAsync(admin, "East Region", "Regional", nation);
        var west = await CreateOrganisationAsync(admin, "West Region", "Regional", nation);

        // Signed in before being appointed: the appointment still counts straight away.
        var email = await api.CreateUserAsync(null);
        var organiser = await api.SignInAsync(email);
        (await organiser.PostAsJsonAsync("/api/tournaments", Tournament(east))).StatusCode.Should().Be(HttpStatusCode.Forbidden);

        var staff = await ReadAsync<List<StaffDto>>(await admin.PutAsJsonAsync($"/api/organisations/{east}/staff", new { email, role = "Organiser" }));
        staff.Should().ContainSingle().Which.Should().BeEquivalentTo(new { Email = email, Role = StaffRole.Organiser }, o => o.ExcludingMissingMembers());

        var appointments = await ReadAsync<List<MyAppointmentDto>>(await organiser.GetAsync("/api/users/me/appointments"));
        appointments.Should().ContainSingle().Which.OrganisationId.Should().Be(east);

        // Below their organisation: allowed. Sideways or upwards: forbidden.
        var club = await CreateOrganisationAsync(organiser, "Eastgate Club", "Club", east);
        (await organiser.PostAsJsonAsync("/api/organisations", new { name = "Westgate Club", level = "Club", parentId = west })).StatusCode.Should().Be(HttpStatusCode.Forbidden);
        (await organiser.PostAsJsonAsync("/api/organisations", new { name = "Another Nation", level = "National" })).StatusCode.Should().Be(HttpStatusCode.Forbidden);
        (await organiser.PostAsJsonAsync("/api/tournaments", Tournament(club))).StatusCode.Should().Be(HttpStatusCode.Created);
        var forbidden = await organiser.PostAsJsonAsync("/api/tournaments", Tournament(west));
        forbidden.StatusCode.Should().Be(HttpStatusCode.Forbidden);
        (await forbidden.Content.ReadAsStringAsync()).Should().Contain("Only an organiser");

        // Staff lists hold email addresses, so they are for that organisation's organisers only.
        (await organiser.GetAsync($"/api/organisations/{club}/staff")).StatusCode.Should().Be(HttpStatusCode.OK);
        (await organiser.GetAsync($"/api/organisations/{west}/staff")).StatusCode.Should().Be(HttpStatusCode.Forbidden);

        // Removing the appointment takes the rights away at once, even though the token is still valid.
        var me = await ReadAsync<UserDto>(await organiser.GetAsync("/api/users/me"));
        (await admin.DeleteAsync($"/api/organisations/{east}/staff/{me.Id}")).StatusCode.Should().Be(HttpStatusCode.OK);
        (await organiser.PostAsJsonAsync("/api/tournaments", Tournament(club))).StatusCode.Should().Be(HttpStatusCode.Forbidden);
    }

    [PostgresFact]
    public async Task Organisers_set_up_venues_tables_and_players()
    {
        var (club, _, _) = await api.AddReferenceDataAsync(0);
        var organiser = await api.ClientForAsync(StaffRole.Organiser, club);

        var venue = await ReadAsync<CreatedResponse>(await organiser.PostAsJsonAsync($"/api/organisations/{club}/venues", new { name = "Riverside Hall", address = "2 Example Lane" }), HttpStatusCode.Created);
        await ReadAsync<CreatedResponse>(await organiser.PostAsJsonAsync($"/api/organisations/venues/{venue.Id}/tables", new { label = "Table A" }));
        (await organiser.PostAsJsonAsync($"/api/organisations/venues/{venue.Id}/tables", new { label = "table a" })).StatusCode.Should().Be(HttpStatusCode.Conflict);

        var created = await organiser.PostAsJsonAsync("/api/players", new { organisationId = club, firstName = "Wren", lastName = "Example", nationality = "ie" });
        created.StatusCode.Should().Be(HttpStatusCode.Created);
        var detail = await ReadAsync<OrganisationDetailDto>(await organiser.GetAsync($"/api/organisations/{club}"));
        detail.MemberCount.Should().Be(1);
        detail.Venues.Should().Contain(v => v.Name == "Riverside Hall" && v.Tables.Count == 1);

        (await organiser.PostAsJsonAsync("/api/players", new { organisationId = club, firstName = "Bad", lastName = "Code", nationality = "GBR" }))
            .StatusCode.Should().Be(HttpStatusCode.BadRequest);
        (await organiser.PostAsJsonAsync("/api/players", new { organisationId = api.FederationId, firstName = "Above", lastName = "Me", nationality = "GB" }))
            .StatusCode.Should().Be(HttpStatusCode.Forbidden);
    }

    [PostgresFact]
    public async Task A_club_referee_scores_their_club_s_matches_and_nothing_else()
    {
        var (club, _, players) = await api.AddReferenceDataAsync(2);
        var organiser = await api.ClientForAsync(StaffRole.Organiser);
        var tournament = await ReadAsync<TournamentDetailDto>(await organiser.PostAsJsonAsync("/api/tournaments", Tournament(club)), HttpStatusCode.Created);
        var id = tournament.Id;
        await organiser.PostAsync($"/api/tournaments/{id}/open-registration", null);
        foreach (var player in players)
        {
            await organiser.PostAsJsonAsync($"/api/tournaments/{id}/entries", new { playerId = player });
        }

        await organiser.PostAsync($"/api/tournaments/{id}/draw?randomSeed=1", null);
        tournament = await ReadAsync<TournamentDetailDto>(await organiser.PostAsync($"/api/tournaments/{id}/start", null));
        var number = tournament.Matches.Single().Number;

        var otherClub = (await api.AddReferenceDataAsync(0)).OrganisationId;
        var outsider = await api.ClientForAsync(StaffRole.Referee, otherClub);
        (await outsider.PostAsync($"/api/tournaments/{id}/matches/{number}/start", null)).StatusCode.Should().Be(HttpStatusCode.Forbidden);

        var referee = await api.ClientForAsync(StaffRole.Referee, club);
        await ReadAsync<TournamentDetailDto>(await referee.PostAsync($"/api/tournaments/{id}/matches/{number}/start", null));
        (await referee.PostAsync($"/api/tournaments/{id}/complete", null)).StatusCode.Should().Be(HttpStatusCode.Forbidden);
    }

    [PostgresFact]
    public async Task Anyone_can_read_the_hierarchy_and_unknown_organisations_are_not_found()
    {
        var (club, _, _) = await api.AddReferenceDataAsync(0);
        var visitor = api.CreateClient();

        var all = await ReadAsync<List<OrganisationSummaryDto>>(await visitor.GetAsync("/api/organisations"));
        all.Should().Contain(o => o.Id == club && o.ParentId == api.FederationId);

        var detail = await ReadAsync<OrganisationDetailDto>(await visitor.GetAsync($"/api/organisations/{api.FederationId}"));
        detail.Children.Should().Contain(c => c.Id == club);
        (await ReadAsync<OrganisationDetailDto>(await visitor.GetAsync($"/api/organisations/{club}"))).Parent!.Id.Should().Be(api.FederationId);

        (await visitor.GetAsync($"/api/organisations/{Guid.NewGuid()}")).StatusCode.Should().Be(HttpStatusCode.NotFound);
        (await visitor.PostAsJsonAsync("/api/organisations", new { name = "Anon", level = "Club", parentId = club })).StatusCode.Should().Be(HttpStatusCode.Unauthorized);
    }
}
