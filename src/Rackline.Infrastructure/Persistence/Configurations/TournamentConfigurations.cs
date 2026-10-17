using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using Rackline.Domain.Matches;
using Rackline.Domain.Organisations;
using Rackline.Domain.Players;
using Rackline.Domain.Rankings;
using Rackline.Domain.Tournaments;

namespace Rackline.Infrastructure.Persistence.Configurations;

internal sealed class TournamentConfiguration : IEntityTypeConfiguration<Tournament>
{
    public void Configure(EntityTypeBuilder<Tournament> builder)
    {
        builder.Property(t => t.Name).HasMaxLength(200);
        builder.Ignore(t => t.Bracket);

        builder.ComplexProperty(t => t.MatchLength, length =>
        {
            length.Property(l => l.Kind).HasColumnName("MatchLengthKind");
            length.Property(l => l.Value).HasColumnName("MatchLengthValue");
        });

        builder.ComplexProperty(t => t.Options, options =>
        {
            options.Property(o => o.GrandFinalReset).HasColumnName("GrandFinalReset");
            options.Property(o => o.GroupCount).HasColumnName("GroupCount");
        });

        builder.HasOne<Organisation>().WithMany()
            .HasForeignKey(t => t.OrganisationId)
            .OnDelete(DeleteBehavior.Restrict);

        builder.HasMany(t => t.Entries).WithOne()
            .HasForeignKey(e => e.TournamentId)
            .OnDelete(DeleteBehavior.Cascade);

        builder.HasMany(t => t.Matches).WithOne()
            .HasForeignKey(m => m.TournamentId)
            .OnDelete(DeleteBehavior.Cascade);

        builder.HasOne<Ranking>().WithMany()
            .HasForeignKey(t => t.RankingId)
            .OnDelete(DeleteBehavior.Restrict);

        builder.HasIndex(t => t.Status);
        builder.HasIndex(t => t.RankingId);

        // Postgres xmin: a save fails if the row changed since it was loaded.
        builder.Property<uint>("Version").IsRowVersion();
    }
}

internal sealed class EntryConfiguration : IEntityTypeConfiguration<Entry>
{
    public void Configure(EntityTypeBuilder<Entry> builder)
    {
        builder.ToTable("Entries");
        builder.HasIndex(e => new { e.TournamentId, e.PlayerId }).IsUnique();
        builder.HasIndex(e => new { e.TournamentId, e.Seed }).IsUnique().HasFilter("\"Seed\" IS NOT NULL");

        builder.HasOne<Player>().WithMany()
            .HasForeignKey(e => e.PlayerId)
            .OnDelete(DeleteBehavior.Restrict);
    }
}

internal sealed class MatchConfiguration : IEntityTypeConfiguration<Match>
{
    public void Configure(EntityTypeBuilder<Match> builder)
    {
        builder.HasIndex(m => new { m.TournamentId, m.Number }).IsUnique();
        builder.HasIndex(m => new { m.TournamentId, m.State });

        // Two results feeding the same next match must not both win silently.
        builder.Property<uint>("Version").IsRowVersion();

        MapSlot(builder, m => m.Slot1, "Slot1");
        MapSlot(builder, m => m.Slot2, "Slot2");
        MapLink(builder, m => m.WinnerTo, "WinnerTo");
        MapLink(builder, m => m.LoserTo, "LoserTo");

        // Entry ids in slots and results are not foreign keys: they always refer to entries of the
        // same tournament, which the domain guarantees and the cascade from Tournament cleans up.
        builder.HasIndex(m => m.WinnerEntryId);
        builder.HasIndex(m => m.LoserEntryId);

        builder.HasOne<Table>().WithMany()
            .HasForeignKey(m => m.TableId)
            .OnDelete(DeleteBehavior.SetNull);

        builder.OwnsMany(m => m.Frames, frames =>
        {
            frames.ToTable("Frames");
            frames.WithOwner().HasForeignKey("MatchId");
            frames.HasKey("MatchId", nameof(Frame.Number));
            frames.Property(f => f.Number).ValueGeneratedNever();
        });
    }

    private static void MapSlot(
        EntityTypeBuilder<Match> builder,
        System.Linq.Expressions.Expression<Func<Match, MatchSlot?>> slot,
        string prefix) =>
        builder.ComplexProperty(slot, s =>
        {
            s.Property(x => x.Kind).HasColumnName($"{prefix}Kind");
            s.Property(x => x.EntryId).HasColumnName($"{prefix}EntryId");
        });

    private static void MapLink(
        EntityTypeBuilder<Match> builder,
        System.Linq.Expressions.Expression<Func<Match, SlotLink?>> link,
        string prefix) =>
        builder.ComplexProperty(link, l =>
        {
            l.Property(x => x.MatchNumber).HasColumnName($"{prefix}MatchNumber");
            l.Property(x => x.Slot).HasColumnName($"{prefix}Slot");
        });
}
