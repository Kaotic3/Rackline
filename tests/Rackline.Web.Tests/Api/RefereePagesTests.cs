using System.Net;
using System.Net.Http.Json;
using System.Text.RegularExpressions;
using FluentAssertions;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.DependencyInjection;
using Rackline.Application.Common;
using Rackline.Infrastructure.Identity;
using Rackline.Web.Tests.Persistence;

namespace Rackline.Web.Tests.Api;

/// <summary>The server-rendered parts of the referee pages, signed in with a cookie through the real form.</summary>
[Trait("Category", "Integration")]
public partial class RefereePagesTests(ApiFactory api) : IClassFixture<ApiFactory>
{
    private HttpClient Browser() => api.CreateClient(new WebApplicationFactoryClientOptions { AllowAutoRedirect = false, HandleCookies = true });

    private async Task<HttpClient> SignedInBrowserAsync(params string[] roles)
    {
        var email = $"page-{Guid.NewGuid():N}@example.test";
        using (var scope = api.Services.CreateScope())
        {
            var users = scope.ServiceProvider.GetRequiredService<UserManager<ApplicationUser>>();
            var user = new ApplicationUser { UserName = email, Email = email };
            (await users.CreateAsync(user, ApiFactory.Password)).Succeeded.Should().BeTrue();
            (await users.AddToRolesAsync(user, roles)).Succeeded.Should().BeTrue();
        }

        var browser = Browser();
        var response = await SubmitLoginAsync(browser, email, ApiFactory.Password, "/referee");
        RedirectTarget(response).Should().Be("/referee");
        return browser;
    }

    private static async Task<HttpResponseMessage> SubmitLoginAsync(HttpClient browser, string email, string password, string returnUrl)
    {
        var form = await browser.GetStringAsync("/account/login");
        var token = AntiforgeryToken().Match(form).Groups[1].Value;

        return await browser.PostAsync($"/account/login?returnUrl={Uri.EscapeDataString(returnUrl)}", new FormUrlEncodedContent(new Dictionary<string, string>
        {
            ["_handler"] = "login",
            ["__RequestVerificationToken"] = token,
            ["Input.Email"] = email,
            ["Input.Password"] = password,
        }));
    }

    /// <summary>A started knockout of four with match 1 ready, created through the API.</summary>
    private async Task<(Guid TournamentId, string TournamentName)> StartedTournamentAsync()
    {
        var (organisationId, _, players) = await api.AddReferenceDataAsync(4);
        var organiser = await api.ClientForAsync(Roles.Organiser);
        var name = $"Page Test Open {Guid.NewGuid():N}"[..24];
        var created = await organiser.PostAsJsonAsync("/api/tournaments", new
        {
            organisationId, name, discipline = "NineBall", format = "SingleElimination", matchLengthKind = "RaceTo", matchLengthValue = 3,
        });
        var id = (await created.Content.ReadFromJsonAsync<IdOnly>())!.Id;
        await organiser.PostAsync($"/api/tournaments/{id}/open-registration", null);
        foreach (var player in players)
        {
            await organiser.PostAsJsonAsync($"/api/tournaments/{id}/entries", new { playerId = player });
        }

        await organiser.PostAsync($"/api/tournaments/{id}/draw?randomSeed=2", null);
        (await organiser.PostAsync($"/api/tournaments/{id}/start", null)).EnsureSuccessStatusCode();
        return (id, name);
    }

    [PostgresFact]
    public async Task Signed_out_visitors_are_sent_to_sign_in()
    {
        var response = await Browser().GetAsync("/referee");

        RedirectTarget(response).Should().Be("/account/login?ReturnUrl=%2Freferee");
    }

    [PostgresFact]
    public async Task A_wrong_password_shows_an_error_and_signs_nobody_in()
    {
        var browser = Browser();

        var response = await SubmitLoginAsync(browser, "nobody@example.test", "Not-The-Password-1", "/referee");

        response.StatusCode.Should().Be(HttpStatusCode.OK);
        (await response.Content.ReadAsStringAsync()).Should().Contain("That email and password do not match an account.");
        (await browser.GetAsync("/referee")).StatusCode.Should().Be(HttpStatusCode.Redirect);
    }

    [PostgresFact]
    public async Task Sign_in_never_redirects_off_the_site()
    {
        var email = $"offsite-{Guid.NewGuid():N}@example.test";
        await api.CreateClient().PostAsJsonAsync("/api/auth/register", new { email, password = ApiFactory.Password });

        var response = await SubmitLoginAsync(Browser(), email, ApiFactory.Password, "//evil.example/steal");

        RedirectTarget(response).Should().Be("/", "the return URL pointed at another site");
    }

    [PostgresFact]
    public async Task Players_without_a_scoring_role_are_turned_away()
    {
        var player = await SignedInBrowserAsync(Roles.Player);

        var response = await player.GetAsync("/referee");

        RedirectTarget(response).Should().StartWith("/account/access-denied");
    }

    [PostgresFact]
    public async Task Referees_see_tournaments_in_progress_and_the_scoring_page()
    {
        var (id, name) = await StartedTournamentAsync();
        var referee = await SignedInBrowserAsync(Roles.Referee);

        var home = await referee.GetStringAsync("/referee");
        var tournament = await referee.GetStringAsync($"/referee/tournaments/{id}");
        var scoring = await referee.GetAsync($"/referee/tournaments/{id}/matches/1");

        home.Should().Contain(name);
        tournament.Should().Contain("Ready to start").And.Contain("Testy McTest");
        scoring.StatusCode.Should().Be(HttpStatusCode.OK);
        var html = await scoring.Content.ReadAsStringAsync();
        html.Should().Contain("Semi-final").And.Contain("Race to 3").And.Contain("Test Hall – Table 1");
    }

    [PostgresFact]
    public async Task Signing_out_ends_the_session()
    {
        var referee = await SignedInBrowserAsync(Roles.Referee);
        var page = await referee.GetStringAsync("/referee");
        var token = AntiforgeryToken().Match(page).Groups[1].Value;

        var signOut = await referee.PostAsync("/account/logout", new FormUrlEncodedContent(new Dictionary<string, string>
        {
            ["__RequestVerificationToken"] = token,
        }));

        RedirectTarget(signOut).Should().Be("/");
        (await referee.GetAsync("/referee")).StatusCode.Should().Be(HttpStatusCode.Redirect);
    }

    /// <summary>The path a redirect points to, after checking it stays on this site.</summary>
    private static string RedirectTarget(HttpResponseMessage response)
    {
        response.StatusCode.Should().Be(HttpStatusCode.Redirect);
        var location = response.Headers.Location!;
        if (!location.IsAbsoluteUri)
        {
            return location.OriginalString;
        }

        location.Host.Should().Be("localhost");
        return location.PathAndQuery;
    }

    private sealed record IdOnly(Guid Id);

    [GeneratedRegex("name=\"__RequestVerificationToken\" value=\"([^\"]+)\"")]
    private static partial Regex AntiforgeryToken();
}
