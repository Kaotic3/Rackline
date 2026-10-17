using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Rackline.Application.Common;
using Rackline.Application.Leagues;
using Rackline.Application.Players;
using Rackline.Application.Rankings;
using Rackline.Application.Tournaments;
using Rackline.Application.Venues;
using Rackline.Infrastructure.Identity;
using Rackline.Infrastructure.Persistence;
using Rackline.Infrastructure.Persistence.Seeding;

namespace Rackline.Infrastructure;

public static class DependencyInjection
{
    public static IServiceCollection AddInfrastructure(this IServiceCollection services, string connectionString)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(connectionString);
        services.AddDbContext<RacklineDbContext>(options => options.UseNpgsql(connectionString));

        services.AddScoped<ITournamentRepository, TournamentRepository>();
        services.AddScoped<IReferenceData, ReferenceData>();
        services.AddScoped<ITournamentQueries, TournamentQueries>();
        services.AddScoped<IPlayerQueries, PlayerQueries>();
        services.AddScoped<IVenueQueries, VenueQueries>();
        services.AddScoped<ILeagueRepository, LeagueRepository>();
        services.AddScoped<ILeagueQueries, LeagueQueries>();
        services.AddScoped<IRankingRepository, RankingRepository>();
        services.AddScoped<IRatingRepository, RatingRepository>();
        services.AddScoped<IRankingQueries, RankingQueries>();
        services.AddScoped<IRatingQueries, RatingQueries>();
        return services;
    }

    /// <summary>
    /// Applies pending migrations, adds invented sample data to an empty database and, when
    /// credentials are supplied, makes sure an administrator exists.
    /// </summary>
    public static async Task MigrateAndSeedAsync(
        this IServiceProvider services,
        (string Email, string Password)? admin = null,
        CancellationToken cancellationToken = default)
    {
        await using var scope = services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<RacklineDbContext>();
        await db.Database.MigrateAsync(cancellationToken);
        await DevelopmentSeeder.SeedAsync(db, cancellationToken);

        if (admin is { } credentials)
        {
            var users = scope.ServiceProvider.GetRequiredService<UserManager<ApplicationUser>>();
            await AdminSeeder.EnsureAdminAsync(users, credentials.Email, credentials.Password);
        }
    }
}
