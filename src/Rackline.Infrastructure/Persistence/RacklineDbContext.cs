using Microsoft.EntityFrameworkCore;
using Rackline.Domain.Challenges;
using Rackline.Domain.Common;
using Rackline.Domain.Leagues;
using Rackline.Domain.Matches;
using Rackline.Domain.Organisations;
using Rackline.Domain.Players;
using Rackline.Domain.Rankings;
using Rackline.Domain.Tournaments;

namespace Rackline.Infrastructure.Persistence;

public sealed class RacklineDbContext(DbContextOptions<RacklineDbContext> options) : DbContext(options)
{
    public DbSet<Organisation> Organisations => Set<Organisation>();

    public DbSet<Venue> Venues => Set<Venue>();

    public DbSet<Player> Players => Set<Player>();

    public DbSet<Tournament> Tournaments => Set<Tournament>();

    public DbSet<Match> Matches => Set<Match>();

    public DbSet<League> Leagues => Set<League>();

    public DbSet<Ranking> Rankings => Set<Ranking>();

    public DbSet<Rating> Ratings => Set<Rating>();

    public DbSet<Challenge> Challenges => Set<Challenge>();

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        modelBuilder.ApplyConfigurationsFromAssembly(typeof(RacklineDbContext).Assembly);

        // Ids are created by the domain, so EF must never treat a set key as "already saved".
        foreach (var entityType in modelBuilder.Model.GetEntityTypes().Where(t => typeof(Entity).IsAssignableFrom(t.ClrType)))
        {
            modelBuilder.Entity(entityType.ClrType).Property(nameof(Entity.Id)).ValueGeneratedNever();
        }
    }

    protected override void ConfigureConventions(ModelConfigurationBuilder configurationBuilder)
    {
        // Store enums as readable text rather than numbers.
        StoreAsString<Discipline>(configurationBuilder);
        StoreAsString<MatchLengthKind>(configurationBuilder);
        StoreAsString<OrganisationLevel>(configurationBuilder);
        StoreAsString<TournamentFormat>(configurationBuilder);
        StoreAsString<TournamentStatus>(configurationBuilder);
        StoreAsString<MatchState>(configurationBuilder);
        StoreAsString<BracketSide>(configurationBuilder);
        StoreAsString<MatchSlotKind>(configurationBuilder);
        StoreAsString<ChallengeStatus>(configurationBuilder);
    }

    private static void StoreAsString<TEnum>(ModelConfigurationBuilder builder)
        where TEnum : struct, Enum =>
        builder.Properties<TEnum>().HaveConversion<string>().HaveMaxLength(32);
}
