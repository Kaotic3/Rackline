using Rackline.Domain.Tournaments;

namespace Rackline.Application.Common;

/// <summary>Loads and saves whole tournament aggregates for changes.</summary>
public interface ITournamentRepository
{
    /// <summary>Returns the tournament with its entries and matches, tracked for changes.</summary>
    Task<Tournament?> FindAsync(Guid id, CancellationToken cancellationToken);

    void Add(Tournament tournament);

    void Remove(Tournament tournament);

    /// <summary>Saves all changes. Throws <see cref="ConflictException"/> if someone else saved first.</summary>
    Task SaveChangesAsync(CancellationToken cancellationToken);
}

/// <summary>Existence checks for things a command refers to but does not change.</summary>
public interface IReferenceData
{
    Task<bool> OrganisationExistsAsync(Guid id, CancellationToken cancellationToken);

    Task<bool> PlayerExistsAsync(Guid id, CancellationToken cancellationToken);

    Task<bool> TableExistsAsync(Guid id, CancellationToken cancellationToken);
}

internal static class TournamentRepositoryExtensions
{
    public static async Task<Tournament> GetAsync(this ITournamentRepository repository, Guid id, CancellationToken cancellationToken) =>
        await repository.FindAsync(id, cancellationToken) ?? throw new NotFoundException("Tournament", id);
}
