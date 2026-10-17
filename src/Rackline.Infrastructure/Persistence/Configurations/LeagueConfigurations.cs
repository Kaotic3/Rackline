using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using Rackline.Domain.Leagues;
using Rackline.Domain.Organisations;
using Rackline.Domain.Players;

namespace Rackline.Infrastructure.Persistence.Configurations;

internal sealed class LeagueConfiguration : IEntityTypeConfiguration<League>
{
    public void Configure(EntityTypeBuilder<League> builder)
    {
        builder.Property(l => l.Name).HasMaxLength(200);

        builder.HasOne<Organisation>().WithMany()
            .HasForeignKey(l => l.OrganisationId)
            .OnDelete(DeleteBehavior.Restrict);

        builder.HasMany(l => l.Seasons).WithOne()
            .HasForeignKey(s => s.LeagueId)
            .OnDelete(DeleteBehavior.Cascade);
    }
}

internal sealed class SeasonConfiguration : IEntityTypeConfiguration<Season>
{
    public void Configure(EntityTypeBuilder<Season> builder)
    {
        builder.ToTable("Seasons");
        builder.Property(s => s.Name).HasMaxLength(100);
        builder.HasIndex(s => s.Status);

        builder.ComplexProperty(s => s.Rules, rules =>
        {
            rules.Property(r => r.RubbersPerFixture).HasColumnName("RubbersPerFixture");
            rules.Property(r => r.PointsForWin).HasColumnName("PointsForWin");
            rules.Property(r => r.PointsForDraw).HasColumnName("PointsForDraw");
            rules.Property(r => r.PointsPerRubber).HasColumnName("PointsPerRubber");
            rules.ComplexProperty(r => r.RubberLength, length =>
            {
                length.Property(l => l.Kind).HasColumnName("RubberLengthKind");
                length.Property(l => l.Value).HasColumnName("RubberLengthValue");
            });
        });

        builder.HasMany(s => s.Teams).WithOne()
            .HasForeignKey(t => t.SeasonId)
            .OnDelete(DeleteBehavior.Cascade);

        builder.HasMany(s => s.Fixtures).WithOne()
            .HasForeignKey(f => f.SeasonId)
            .OnDelete(DeleteBehavior.Cascade);
    }
}

internal sealed class TeamConfiguration : IEntityTypeConfiguration<Team>
{
    public void Configure(EntityTypeBuilder<Team> builder)
    {
        builder.ToTable("Teams");
        builder.Property(t => t.Name).HasMaxLength(100);
        builder.HasIndex(t => new { t.SeasonId, t.Name }).IsUnique();

        builder.PrimitiveCollection(t => t.PlayerIds);
        builder.HasIndex(t => t.PlayerIds).HasMethod("gin");

        builder.HasOne<Venue>().WithMany()
            .HasForeignKey(t => t.HomeVenueId)
            .OnDelete(DeleteBehavior.SetNull);
    }
}

internal sealed class FixtureConfiguration : IEntityTypeConfiguration<Fixture>
{
    public void Configure(EntityTypeBuilder<Fixture> builder)
    {
        builder.ToTable("Fixtures");
        builder.HasIndex(f => new { f.SeasonId, f.Round });
        builder.HasIndex(f => f.Status);

        // Postgres xmin: two people entering the same result cannot overwrite each other unseen.
        builder.Property<uint>("Version").IsRowVersion();

        builder.HasOne<Team>().WithMany()
            .HasForeignKey(f => f.HomeTeamId)
            .OnDelete(DeleteBehavior.Restrict);

        builder.HasOne<Team>().WithMany()
            .HasForeignKey(f => f.AwayTeamId)
            .OnDelete(DeleteBehavior.Restrict);

        builder.HasIndex(f => f.ScheduledFor);

        builder.OwnsMany(f => f.Rubbers, rubbers =>
        {
            rubbers.ToTable("Rubbers");
            rubbers.WithOwner().HasForeignKey("FixtureId");
            rubbers.HasKey("FixtureId", nameof(Rubber.Number));
            rubbers.Property(r => r.Number).ValueGeneratedNever();

            rubbers.HasOne<Player>().WithMany().HasForeignKey(r => r.HomePlayerId).OnDelete(DeleteBehavior.Restrict);
            rubbers.HasOne<Player>().WithMany().HasForeignKey(r => r.AwayPlayerId).OnDelete(DeleteBehavior.Restrict);
        });
    }
}
