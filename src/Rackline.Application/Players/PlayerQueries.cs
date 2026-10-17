namespace Rackline.Application.Players;

public sealed record PlayerSummaryDto(Guid Id, string FirstName, string LastName, string Nationality, string? LicenceNumber);

public interface IPlayerQueries
{
    /// <summary>Players whose name contains <paramref name="search"/> (case-insensitive), or all players.</summary>
    Task<IReadOnlyList<PlayerSummaryDto>> ListAsync(string? search, CancellationToken cancellationToken);
}
