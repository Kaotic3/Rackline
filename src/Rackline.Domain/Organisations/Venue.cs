using Rackline.Domain.Common;

namespace Rackline.Domain.Organisations;

public sealed class Venue : Entity
{
    private readonly List<Table> _tables = [];

    public Venue(Guid organisationId, string name, string? address = null)
    {
        OrganisationId = Guard.NotEmpty(organisationId, "Organisation");
        Name = Guard.NotBlank(name, "Venue name");
        Address = string.IsNullOrWhiteSpace(address) ? null : address.Trim();
    }

    public Guid OrganisationId { get; private set; }

    public string Name { get; private set; }

    public string? Address { get; private set; }

    public IReadOnlyList<Table> Tables => _tables;

    public Table AddTable(string label)
    {
        label = Guard.NotBlank(label, "Table label");
        Guard.That(
            !_tables.Any(t => string.Equals(t.Label, label, StringComparison.OrdinalIgnoreCase)),
            $"Venue already has a table labelled '{label}'.");

        var table = new Table(Id, label);
        _tables.Add(table);
        return table;
    }
}

public sealed class Table : Entity
{
    internal Table(Guid venueId, string label)
    {
        VenueId = venueId;
        Label = label;
    }

    public Guid VenueId { get; private set; }

    public string Label { get; private set; }
}
