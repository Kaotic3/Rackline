using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Rackline.Infrastructure.Persistence;
using Rackline.Infrastructure.Persistence.Seeding;

namespace Rackline.Infrastructure;

public static class DependencyInjection
{
    public static IServiceCollection AddInfrastructure(this IServiceCollection services, string connectionString)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(connectionString);
        services.AddDbContext<RacklineDbContext>(options => options.UseNpgsql(connectionString));
        return services;
    }

    /// <summary>Applies pending migrations and, when the database is empty, adds invented sample data.</summary>
    public static async Task MigrateAndSeedAsync(this IServiceProvider services, CancellationToken cancellationToken = default)
    {
        await using var scope = services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<RacklineDbContext>();
        await db.Database.MigrateAsync(cancellationToken);
        await DevelopmentSeeder.SeedAsync(db, cancellationToken);
    }
}
