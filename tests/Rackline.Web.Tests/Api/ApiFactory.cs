using FluentAssertions;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Rackline.Domain.Organisations;
using Rackline.Domain.Players;
using Rackline.Infrastructure.Identity;
using Rackline.Infrastructure.Persistence;
using Rackline.Web.Tests.Persistence;

namespace Rackline.Web.Tests.Api;

/// <summary>The real app, running against a throwaway Postgres database with a fixed clock.</summary>
public sealed partial class ApiFactory : WebApplicationFactory<Program>, IAsyncLifetime
{
    public const string Password = "Correct-Horse-9";

    private readonly PostgresDatabase _database = new();

    public TestClock Clock { get; } = new(new DateTimeOffset(2026, 7, 4, 18, 0, 0, TimeSpan.Zero));

    protected override void ConfigureWebHost(IWebHostBuilder builder)
    {
        builder.UseEnvironment("Testing");
        builder.UseSetting("ConnectionStrings:Rackline", _database.ConnectionString);
        builder.UseSetting("Database:MigrateAndSeedOnStartup", "false");
        builder.ConfigureServices(services => services.Replace(ServiceDescriptor.Singleton<TimeProvider>(Clock)));
    }

    /// <summary>
    /// A top-level organisation over every club the tests add, so staff appointed here (the default)
    /// can act anywhere in the test data. Tests of the hierarchy itself add their own.
    /// </summary>
    public Guid FederationId { get; private set; }

    public async Task InitializeAsync()
    {
        await _database.InitializeAsync();
        var federation = new Organisation($"Test Federation {Guid.NewGuid():N}", OrganisationLevel.National);
        await using var db = CreateDbContext();
        db.Organisations.Add(federation);
        await db.SaveChangesAsync();
        FederationId = federation.Id;
    }

    public new async Task DisposeAsync()
    {
        await base.DisposeAsync();
        await _database.DisposeAsync();
    }

    public RacklineDbContext CreateDbContext() => _database.CreateContext();

    /// <summary>Creates a user with the given site-wide roles and returns a client signed in as them.</summary>
    public async Task<HttpClient> ClientForAsync(params string[] roles) => await SignInAsync(await CreateUserAsync(null, null, roles));

    /// <summary>
    /// Creates a user appointed with <paramref name="role"/> at the organisation (by default the
    /// test federation, which is above every club the tests add) and returns a client signed in as them.
    /// </summary>
    public async Task<HttpClient> ClientForAsync(StaffRole role, Guid? organisationId = null) =>
        await SignInAsync(await CreateUserAsync(role, organisationId));

    /// <summary>Creates a user, optionally with an appointment and site-wide roles, and returns their email.</summary>
    public async Task<string> CreateUserAsync(StaffRole? role, Guid? organisationId = null, params string[] roles)
    {
        var email = $"user-{Guid.NewGuid():N}@example.test";
        using var scope = Services.CreateScope();
        var users = scope.ServiceProvider.GetRequiredService<UserManager<ApplicationUser>>();
        var user = new ApplicationUser { UserName = email, Email = email };
        (await users.CreateAsync(user, Password)).Succeeded.Should().BeTrue();
        (await users.AddToRolesAsync(user, roles)).Succeeded.Should().BeTrue();

        if (role is { } staffRole)
        {
            await using var db = CreateDbContext();
            db.Appointments.Add(new Appointment(organisationId ?? FederationId, user.Id, staffRole));
            await db.SaveChangesAsync();
        }

        return email;
    }

    /// <summary>A cookie-holding client signed in through the real sign-in form, for page tests.</summary>
    public async Task<HttpClient> BrowserSignedInAsync(string email)
    {
        var browser = CreateClient(new WebApplicationFactoryClientOptions { AllowAutoRedirect = false, HandleCookies = true });
        var form = await browser.GetStringAsync("/account/login");
        var token = AntiforgeryToken().Match(form).Groups[1].Value;
        var response = await browser.PostAsync("/account/login?returnUrl=%2F", new FormUrlEncodedContent(new Dictionary<string, string>
        {
            ["_handler"] = "login",
            ["__RequestVerificationToken"] = token,
            ["Input.Email"] = email,
            ["Input.Password"] = Password,
        }));
        response.StatusCode.Should().Be(System.Net.HttpStatusCode.Redirect);
        return browser;
    }

    [System.Text.RegularExpressions.GeneratedRegex("name=\"__RequestVerificationToken\" value=\"([^\"]+)\"")]
    private static partial System.Text.RegularExpressions.Regex AntiforgeryToken();

    public async Task<HttpClient> SignInAsync(string email)
    {
        var client = CreateClient();
        var response = await client.PostAsJsonAsync("/api/auth/login", new { email, password = Password });
        response.EnsureSuccessStatusCode();
        var token = await response.Content.ReadFromJsonAsync<TokenResponse>();
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", token!.AccessToken);
        return client;
    }

    /// <summary>Adds a club under the test federation, a venue with one table and some invented players.</summary>
    public async Task<(Guid OrganisationId, Guid TableId, List<Guid> PlayerIds)> AddReferenceDataAsync(int players)
    {
        await using var lookup = CreateDbContext();
        var federation = await lookup.Organisations.SingleAsync(o => o.Id == FederationId);
        var organisation = new Organisation($"Test Club {Guid.NewGuid():N}", OrganisationLevel.Club, federation);
        var venue = new Venue(organisation.Id, "Test Hall");
        var table = venue.AddTable("Table 1");
        var people = Enumerable.Range(1, players).Select(i => new Player("Testy", $"McTest{i}", "GB")).ToList();

        await using var db = CreateDbContext();
        db.Organisations.Add(organisation);
        db.Venues.Add(venue);
        db.Players.AddRange(people);
        await db.SaveChangesAsync();
        return (organisation.Id, table.Id, people.Select(p => p.Id).ToList());
    }

    private sealed record TokenResponse(string AccessToken);
}

public sealed class TestClock(DateTimeOffset now) : TimeProvider
{
    public DateTimeOffset Now { get; set; } = now;

    public override DateTimeOffset GetUtcNow() => Now;
}
