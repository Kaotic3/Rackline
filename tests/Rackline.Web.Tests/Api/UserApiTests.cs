using System.Net;
using System.Net.Http.Json;
using FluentAssertions;
using Rackline.Application.Common;
using Rackline.Domain.Organisations;
using Rackline.Web.Tests.Persistence;

namespace Rackline.Web.Tests.Api;

[Trait("Category", "Integration")]
public class UserApiTests(ApiFactory api) : IClassFixture<ApiFactory>
{
    private sealed record User(Guid Id, string Email, string[] Roles);

    [PostgresFact]
    public async Task New_users_can_register_and_sign_in_without_any_roles()
    {
        var email = $"new-{Guid.NewGuid():N}@example.test";
        var anonymous = api.CreateClient();

        (await anonymous.PostAsJsonAsync("/api/auth/register", new { email, password = ApiFactory.Password }))
            .StatusCode.Should().Be(HttpStatusCode.OK);

        var client = await api.SignInAsync(email);
        var me = await client.GetFromJsonAsync<User>("/api/users/me");

        me!.Email.Should().Be(email);
        me.Roles.Should().BeEmpty();
    }

    [PostgresFact]
    public async Task Short_passwords_are_refused()
    {
        var response = await api.CreateClient().PostAsJsonAsync("/api/auth/register",
            new { email = $"short-{Guid.NewGuid():N}@example.test", password = "Ab1!" });

        response.StatusCode.Should().Be(HttpStatusCode.BadRequest);
    }

    [PostgresFact]
    public async Task Signing_in_is_needed_to_see_yourself()
    {
        (await api.CreateClient().GetAsync("/api/users/me")).StatusCode.Should().Be(HttpStatusCode.Unauthorized);
    }

    [PostgresFact]
    public async Task Admins_assign_roles_and_the_new_roles_apply_after_signing_in_again()
    {
        var admin = await api.ClientForAsync(Roles.Admin);
        var member = await api.ClientForAsync();
        var memberEmail = (await member.GetFromJsonAsync<User>("/api/users/me"))!.Email;

        var found = await admin.GetFromJsonAsync<List<User>>($"/api/users?email={memberEmail}");
        var userId = found!.Single().Id;

        var assigned = await admin.PutAsJsonAsync($"/api/users/{userId}/roles", new { roles = new[] { "admin", "Player" } });
        assigned.StatusCode.Should().Be(HttpStatusCode.OK);
        (await assigned.Content.ReadFromJsonAsync<User>())!.Roles.Should().BeEquivalentTo(Roles.Admin, Roles.Player);

        var reSignedIn = await api.SignInAsync(memberEmail);
        (await reSignedIn.GetFromJsonAsync<User>("/api/users/me"))!.Roles.Should().BeEquivalentTo(Roles.Admin, Roles.Player);

        var reduced = await admin.PutAsJsonAsync($"/api/users/{userId}/roles", new { roles = new[] { Roles.Player } });
        (await reduced.Content.ReadFromJsonAsync<User>())!.Roles.Should().Equal(Roles.Player);
    }

    [PostgresFact]
    public async Task Only_admins_manage_roles_and_only_known_roles_are_accepted()
    {
        var admin = await api.ClientForAsync(Roles.Admin);
        var organiser = await api.ClientForAsync(StaffRole.Organiser);
        var someone = Guid.NewGuid();

        (await organiser.PutAsJsonAsync($"/api/users/{someone}/roles", new { roles = new[] { Roles.Admin } }))
            .StatusCode.Should().Be(HttpStatusCode.Forbidden);
        (await organiser.GetAsync("/api/users")).StatusCode.Should().Be(HttpStatusCode.Forbidden);
        (await admin.PutAsJsonAsync($"/api/users/{someone}/roles", new { roles = new[] { "Wizard" } }))
            .StatusCode.Should().Be(HttpStatusCode.BadRequest);

        // Organisers and referees are appointed at an organisation, not given a site-wide role.
        (await admin.PutAsJsonAsync($"/api/users/{someone}/roles", new { roles = new[] { "Organiser" } }))
            .StatusCode.Should().Be(HttpStatusCode.BadRequest);
        (await admin.PutAsJsonAsync($"/api/users/{someone}/roles", new { roles = new[] { Roles.Player } }))
            .StatusCode.Should().Be(HttpStatusCode.NotFound);
    }
}
