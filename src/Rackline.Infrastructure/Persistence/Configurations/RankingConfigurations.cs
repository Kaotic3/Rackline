using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using Rackline.Domain.Challenges;
using Rackline.Domain.Organisations;
using Rackline.Domain.Players;
using Rackline.Domain.Rankings;

namespace Rackline.Infrastructure.Persistence.Configurations;

internal sealed class RankingConfiguration : IEntityTypeConfiguration<Ranking>
{
    public void Configure(EntityTypeBuilder<Ranking> builder)
    {
        builder.Property(r => r.Season).HasMaxLength(50);
        builder.HasIndex(r => new { r.OrganisationId, r.Discipline, r.Season }).IsUnique();

        builder.HasOne<Organisation>().WithMany()
            .HasForeignKey(r => r.OrganisationId)
            .OnDelete(DeleteBehavior.Restrict);

        builder.OwnsMany(r => r.Entries, entries =>
        {
            entries.ToTable("RankingEntries");
            entries.WithOwner().HasForeignKey("RankingId");
            entries.HasKey("RankingId", nameof(RankingEntry.PlayerId));

            entries.HasOne<Player>().WithMany().HasForeignKey(e => e.PlayerId).OnDelete(DeleteBehavior.Cascade);
            entries.HasIndex(e => e.PlayerId);
        });
    }
}

internal sealed class RatingConfiguration : IEntityTypeConfiguration<Rating>
{
    public void Configure(EntityTypeBuilder<Rating> builder)
    {
        builder.HasIndex(r => new { r.PlayerId, r.Discipline }).IsUnique();

        builder.HasOne<Player>().WithMany()
            .HasForeignKey(r => r.PlayerId)
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

        builder.HasOne<Player>().WithMany()
            .HasForeignKey(c => c.ChallengerId)
            .OnDelete(DeleteBehavior.Restrict);

        builder.HasOne<Player>().WithMany()
            .HasForeignKey(c => c.OpponentId)
            .OnDelete(DeleteBehavior.Restrict);

        builder.HasIndex(c => c.Status);
    }
}
