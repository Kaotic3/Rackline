using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using Rackline.Domain.Organisations;
using Rackline.Domain.Players;

namespace Rackline.Infrastructure.Persistence.Configurations;

internal sealed class OrganisationConfiguration : IEntityTypeConfiguration<Organisation>
{
    public void Configure(EntityTypeBuilder<Organisation> builder)
    {
        builder.Property(o => o.Name).HasMaxLength(200);
        builder.HasIndex(o => o.Level);

        builder.HasOne<Organisation>().WithMany()
            .HasForeignKey(o => o.ParentId)
            .OnDelete(DeleteBehavior.Restrict);
    }
}

internal sealed class VenueConfiguration : IEntityTypeConfiguration<Venue>
{
    public void Configure(EntityTypeBuilder<Venue> builder)
    {
        builder.Property(v => v.Name).HasMaxLength(200);
        builder.Property(v => v.Address).HasMaxLength(500);

        builder.HasOne<Organisation>().WithMany()
            .HasForeignKey(v => v.OrganisationId)
            .OnDelete(DeleteBehavior.Restrict);

        builder.HasMany(v => v.Tables).WithOne()
            .HasForeignKey(t => t.VenueId)
            .OnDelete(DeleteBehavior.Cascade);
    }
}

internal sealed class TableConfiguration : IEntityTypeConfiguration<Table>
{
    public void Configure(EntityTypeBuilder<Table> builder)
    {
        builder.ToTable("Tables");
        builder.Property(t => t.Label).HasMaxLength(50);
        builder.HasIndex(t => new { t.VenueId, t.Label }).IsUnique();
    }
}

internal sealed class PlayerConfiguration : IEntityTypeConfiguration<Player>
{
    public void Configure(EntityTypeBuilder<Player> builder)
    {
        builder.Property(p => p.FirstName).HasMaxLength(100);
        builder.Property(p => p.LastName).HasMaxLength(100);
        builder.Property(p => p.Nationality).HasMaxLength(2).IsFixedLength();
        builder.Property(p => p.LicenceNumber).HasMaxLength(50);

        // Stored as a uuid[] column; the GIN index supports "players in organisation X" queries.
        builder.PrimitiveCollection(p => p.OrganisationIds);
        builder.HasIndex(p => p.OrganisationIds).HasMethod("gin");

        builder.HasIndex(p => new { p.LastName, p.FirstName });
        builder.HasIndex(p => p.LicenceNumber);
    }
}
