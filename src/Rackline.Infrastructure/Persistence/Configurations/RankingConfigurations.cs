using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using Rackline.Domain.Challenges;
using Rackline.Domain.Organisations;
using Rackline.Domain.Players;
using Rackline.Domain.Rankings;
using Rackline.Domain.Tournaments;

namespace Rackline.Infrastructure.Persistence.Configurations;

internal sealed class RankingConfiguration : IEntityTypeConfiguration<Ranking>
{
    public void Configure(EntityTypeBuilder<Ranking> builder)
    {
        builder.Property(r => r.Name).HasMaxLength(200);
        builder.Property(r => r.Season).HasMaxLength(50);
        builder.PrimitiveCollection(r => r.PointsByPosition);
        builder.HasIndex(r => new { r.OrganisationId, r.Discipline, r.Season }).IsUnique();

        builder.HasOne<Organisation>().WithMany()
            .HasForeignKey(r => r.OrganisationId)
            .OnDelete(DeleteBehavior.Restrict);

        // The key includes the tournament, so a tournament racing to be counted twice fails to save.
        builder.OwnsMany(r => r.Awards, awards =>
        {
            awards.ToTable("RankingAwards");
            awards.WithOwner().HasForeignKey("RankingId");
            awards.HasKey("RankingId", nameof(RankingAward.TournamentId), nameof(RankingAward.PlayerId));

            awards.HasOne<Player>().WithMany().HasForeignKey(a => a.PlayerId).OnDelete(DeleteBehavior.Cascade);
            awards.HasOne<Tournament>().WithMany().HasForeignKey(a => a.TournamentId).OnDelete(DeleteBehavior.Restrict);
            awards.HasIndex(a => a.PlayerId);
            awards.HasIndex(a => a.TournamentId);
        });
    }
}

internal sealed class RatingConfiguration : IEntityTypeConfiguration<Rating>
{
    public void Configure(EntityTypeBuilder<Rating> builder)
    {
        builder.HasIndex(r => new { r.PlayerId, r.Discipline }).IsUnique();
        builder.HasIndex(r => new { r.Discipline, r.Value });

        builder.HasOne<Player>().WithMany()
            .HasForeignKey(r => r.PlayerId)
            .OnDelete(DeleteBehavior.Cascade);

        // Postgres xmin: two results for the same player cannot both move their rating unseen.
        builder.Property<uint>("Version").IsRowVersion();
    }
}

internal sealed class RatingChangeConfiguration : IEntityTypeConfiguration<RatingChange>
{
    public void Configure(EntityTypeBuilder<RatingChange> builder)
    {
        // A match can move each player's rating only once. MatchId is not a foreign key: the history
        // stays if a cancelled tournament is later deleted.
        builder.HasIndex(c => new { c.PlayerId, c.MatchId }).IsUnique();
        builder.HasIndex(c => new { c.PlayerId, c.Discipline, c.At });
        builder.HasIndex(c => c.MatchId);
        builder.HasIndex(c => c.OpponentId);

        builder.HasOne<Player>().WithMany()
            .HasForeignKey(c => c.PlayerId)
            .OnDelete(DeleteBehavior.Cascade);

        builder.HasOne<Player>().WithMany()
            .HasForeignKey(c => c.OpponentId)
            .OnDelete(DeleteBehavior.Cascade);
    }
}

internal sealed class ChallengeConfiguration : IEntityTypeConfiguration<Challenge>
{
    public void Configure(EntityTypeBuilder<Challenge> builder)
    {
        builder.ComplexProperty(c => c.MatchLength, length =>
        {
            length.Property(l => l.Kind).HasColumnName("MatchLengthKind");
            length.Property(l => l.Value).HasColumnName("MatchLengthValue");
        });

        builder.Property(c => c.UnratedReason).HasMaxLength(500);

        builder.HasOne<Organisation>().WithMany()
            .HasForeignKey(c => c.OrganisationId)
            .OnDelete(DeleteBehavior.Restrict);

        builder.HasOne<Player>().WithMany()
            .HasForeignKey(c => c.Player1Id)
            .OnDelete(DeleteBehavior.Restrict);

        builder.HasOne<Player>().WithMany()
            .HasForeignKey(c => c.Player2Id)
            .OnDelete(DeleteBehavior.Restrict);

        builder.HasIndex(c => c.PlayedAt);
        builder.HasIndex(c => new { c.Player1Id, c.Player2Id, c.Discipline });
    }
}
