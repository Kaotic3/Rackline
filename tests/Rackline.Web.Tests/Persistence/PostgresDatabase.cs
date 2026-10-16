using Microsoft.EntityFrameworkCore;
using Npgsql;
using Rackline.Infrastructure.Persistence;

namespace Rackline.Web.Tests.Persistence;

/// <summary>
/// Skips the test unless <c>RACKLINE_TEST_CONNECTION</c> points at a Postgres server
/// (e.g. the one from docker-compose.yml).
/// </summary>
public sealed class PostgresFactAttribute : FactAttribute
{
    public PostgresFactAttribute()
    {
        if (string.IsNullOrWhiteSpace(PostgresDatabase.ServerConnectionString))
        {
            Skip = $"Set {PostgresDatabase.ConnectionVariable} to run Postgres integration tests.";
        }
    }
}

/// <summary>A throwaway, fully migrated database for one test class.</summary>
public sealed class PostgresDatabase : IAsyncLifetime
{
    public const string ConnectionVariable = "RACKLINE_TEST_CONNECTION";

    private string? _connectionString;

    public static string? ServerConnectionString => Environment.GetEnvironmentVariable(ConnectionVariable);

    public RacklineDbContext CreateContext()
    {
        var options = new DbContextOptionsBuilder<RacklineDbContext>()
            .UseNpgsql(_connectionString ?? throw new InvalidOperationException("The test database is not available."))
            .Options;
        return new RacklineDbContext(options);
    }

    public async Task InitializeAsync()
    {
        if (string.IsNullOrWhiteSpace(ServerConnectionString))
        {
            return;
        }

        _connectionString = new NpgsqlConnectionStringBuilder(ServerConnectionString)
        {
            Database = $"rackline_test_{Guid.NewGuid():N}",
        }.ConnectionString;

        await using var db = CreateContext();
        await db.Database.MigrateAsync();
    }

    public async Task DisposeAsync()
    {
        if (_connectionString is null)
        {
            return;
        }

        await using var db = CreateContext();
        await db.Database.EnsureDeletedAsync();
    }
}
