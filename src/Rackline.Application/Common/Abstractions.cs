using Rackline.Domain.Leagues;
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

    Task<bool> VenueExistsAsync(Guid id, CancellationToken cancellationToken);
}

/// <summary>Loads and saves whole league aggregates (seasons, teams, fixtures and rubbers) for changes.</summary>
public interface ILeagueRepository
{
    Task<League?> FindAsync(Guid leagueId, CancellationToken cancellationToken);

    /// <summary>The league that owns the season, tracked for changes.</summary>
    Task<League?> FindBySeasonAsync(Guid seasonId, CancellationToken cancellationToken);

    void Add(League league);

    /// <summary>Saves all changes. Throws <see cref="ConflictException"/> if someone else saved first.</summary>
    Task SaveChangesAsync(CancellationToken cancellationToken);
}

internal static class TournamentRepositoryExtensions
{
    public static async Task<Tournament> GetAsync(this ITournamentRepository repository, Guid id, CancellationToken cancellationToken) =>
        await repository.FindAsync(id, cancellationToken) ?? throw new NotFoundException("Tournament", id);

    public static async Task<Tournament> GetWithMatchAsync(this ITournamentRepository repository, Guid id, int matchNumber, CancellationToken cancellationToken)
    {
        var tournament = await repository.GetAsync(id, cancellationToken);
        return tournament.Matches.Any(m => m.Number == matchNumber)
            ? tournament
            : throw new NotFoundException("Match", matchNumber);
    }
}
