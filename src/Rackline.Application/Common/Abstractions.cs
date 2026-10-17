using Rackline.Domain.Common;
using Rackline.Domain.Leagues;
using Rackline.Domain.Rankings;
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

/// <summary>Loads and saves rankings and the points they have awarded.</summary>
/// <remarks>Repositories share one unit of work per request, so a tournament command can award points and save once.</remarks>
public interface IRankingRepository
{
    Task<Ranking?> FindAsync(Guid id, CancellationToken cancellationToken);

    void Add(Ranking ranking);

    /// <summary>Saves all changes. Throws <see cref="ConflictException"/> if someone else saved first.</summary>
    Task SaveChangesAsync(CancellationToken cancellationToken);
}

/// <summary>Players' ratings and their history.</summary>
/// <remarks>
/// Changes are saved by the command's next save on any repository: they share one unit of work, so
/// a result and the ratings it moves are saved together or not at all.
/// </remarks>
public interface IRatingRepository
{
    /// <summary>The player's rating in the discipline, tracked for changes; null if they have never been rated.</summary>
    Task<Rating?> FindAsync(Guid playerId, Discipline discipline, CancellationToken cancellationToken);

    void Add(Rating rating);

    void Add(RatingChange change);

    /// <summary>Every tournament result that counts towards ratings, oldest first.</summary>
    Task<IReadOnlyList<RatedResult>> ListRatedResultsAsync(CancellationToken cancellationToken);

    /// <summary>Replaces every rating and all rating history in one transaction, and saves.</summary>
    Task ReplaceAllAsync(IReadOnlyList<Rating> ratings, IReadOnlyList<RatingChange> changes, CancellationToken cancellationToken);
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
