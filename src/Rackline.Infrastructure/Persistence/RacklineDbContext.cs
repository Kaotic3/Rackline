using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Identity.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore;
using Rackline.Domain.Challenges;
using Rackline.Domain.Common;
using Rackline.Domain.Leagues;
using Rackline.Domain.Matches;
using Rackline.Domain.Organisations;
using Rackline.Domain.Players;
using Rackline.Domain.Rankings;
using Rackline.Domain.Tournaments;
using Rackline.Infrastructure.Identity;

namespace Rackline.Infrastructure.Persistence;

public sealed class RacklineDbContext(DbContextOptions<RacklineDbContext> options)
    : IdentityDbContext<ApplicationUser, IdentityRole<Guid>, Guid>(options)
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
        base.OnModelCreating(modelBuilder);
        modelBuilder.ApplyConfigurationsFromAssembly(typeof(RacklineDbContext).Assembly);

        // Roles are reference data, so every environment gets them from the migration.
        modelBuilder.Entity<IdentityRole<Guid>>().HasData(Application.Common.Roles.All.Select(r => new IdentityRole<Guid>
        {
            Id = r.Id,
            Name = r.Name,
            NormalizedName = r.Name.ToUpperInvariant(),
            ConcurrencyStamp = r.Id.ToString(),
        }));

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
