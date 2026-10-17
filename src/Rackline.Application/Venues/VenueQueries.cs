namespace Rackline.Application.Venues;

public sealed record TableDto(Guid Id, string Label, Guid VenueId, string VenueName);

public interface IVenueQueries
{
    /// <summary>Every table, grouped by venue name.</summary>
    Task<IReadOnlyList<TableDto>> ListTablesAsync(CancellationToken cancellationToken);
}
