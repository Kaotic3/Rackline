using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Metadata;
using Rackline.Domain.Common;
using Rackline.Infrastructure.Persistence;

namespace Rackline.Web.Tests.Persistence;

/// <summary>Checks on the EF Core model that need no database.</summary>
public class ModelTests
{
    private static RacklineDbContext CreateContext() =>
        new(new DbContextOptionsBuilder<RacklineDbContext>().UseNpgsql("Host=unused").Options);

    private static IModel DesignModel()
    {
        using var db = CreateContext();
        return db.GetService<IDesignTimeModel>().Model;
    }

    [Fact]
    public void Migrations_are_up_to_date_with_the_model()
    {
        using var db = CreateContext();

        db.Database.HasPendingModelChanges().Should().BeFalse("run 'dotnet ef migrations add' after changing the model");
    }

    [Fact]
    public void Every_foreign_key_is_indexed()
    {
        var unindexed =
            from entity in DesignModel().GetEntityTypes()
            from foreignKey in entity.GetForeignKeys()
            let columns = foreignKey.Properties
            where !entity.GetIndexes().Any(i => i.Properties.Take(columns.Count).SequenceEqual(columns))
               && !(entity.FindPrimaryKey()?.Properties.Take(columns.Count).SequenceEqual(columns) ?? false)
            select $"{entity.DisplayName()}({string.Join(", ", columns.Select(c => c.Name))})";

        unindexed.Should().BeEmpty();
    }

    [Fact]
    public void Domain_ids_are_never_generated_by_the_database()
    {
        var generated = DesignModel().GetEntityTypes()
            .Where(t => typeof(Entity).IsAssignableFrom(t.ClrType))
            .Where(t => t.FindProperty(nameof(Entity.Id))!.ValueGenerated != ValueGenerated.Never)
            .Select(t => t.DisplayName());

        generated.Should().BeEmpty();
    }

    [Fact]
    public void Enums_are_stored_as_text()
    {
        static IEnumerable<IProperty> AllProperties(ITypeBase type) =>
            type.GetProperties().Concat(type.GetComplexProperties().SelectMany(c => AllProperties(c.ComplexType)));

        var numericEnums = DesignModel().GetEntityTypes()
            .SelectMany(AllProperties)
            .Where(p => (Nullable.GetUnderlyingType(p.ClrType) ?? p.ClrType).IsEnum)
            .Where(p => p.GetProviderClrType() != typeof(string))
            .Select(p => $"{p.DeclaringType.DisplayName()}.{p.Name}");

        numericEnums.Should().BeEmpty();
    }
}
