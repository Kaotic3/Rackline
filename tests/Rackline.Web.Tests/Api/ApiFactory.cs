using FluentAssertions;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Rackline.Domain.Organisations;
using Rackline.Domain.Players;
using Rackline.Infrastructure.Identity;
using Rackline.Infrastructure.Persistence;
using Rackline.Web.Tests.Persistence;

namespace Rackline.Web.Tests.Api;

/// <summary>The real app, running against a throwaway Postgres database with a fixed clock.</summary>
public sealed class ApiFactory : WebApplicationFactory<Program>, IAsyncLifetime
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

    public Task InitializeAsync() => _database.InitializeAsync();

    public new async Task DisposeAsync()
    {
        await base.DisposeAsync();
        await _database.DisposeAsync();
    }

    public RacklineDbContext CreateDbContext() => _database.CreateContext();

    /// <summary>Creates a user with the given roles and returns a client signed in as them.</summary>
    public async Task<HttpClient> ClientForAsync(params string[] roles)
    {
        var email = $"user-{Guid.NewGuid():N}@example.test";
        using (var scope = Services.CreateScope())
        {
            var users = scope.ServiceProvider.GetRequiredService<UserManager<ApplicationUser>>();
            var user = new ApplicationUser { UserName = email, Email = email };
            (await users.CreateAsync(user, Password)).Succeeded.Should().BeTrue();
            (await users.AddToRolesAsync(user, roles)).Succeeded.Should().BeTrue();
        }

        return await SignInAsync(email);
    }

    public async Task<HttpClient> SignInAsync(string email)
    {
        var client = CreateClient();
        var response = await client.PostAsJsonAsync("/api/auth/login", new { email, password = Password });
        response.EnsureSuccessStatusCode();
        var token = await response.Content.ReadFromJsonAsync<TokenResponse>();
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", token!.AccessToken);
        return client;
    }

    /// <summary>Adds an organisation, a venue with one table and some invented players.</summary>
    public async Task<(Guid OrganisationId, Guid TableId, List<Guid> PlayerIds)> AddReferenceDataAsync(int players)
    {
        var organisation = new Organisation($"Test Club {Guid.NewGuid():N}", OrganisationLevel.Club);
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
