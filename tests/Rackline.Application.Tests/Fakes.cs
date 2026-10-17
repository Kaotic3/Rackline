using Rackline.Application.Common;
using Rackline.Application.Live;
using Rackline.Application.Tournaments;
using Rackline.Domain.Common;
using Rackline.Domain.Leagues;
using Rackline.Domain.Rankings;
using Rackline.Domain.Tournaments;

namespace Rackline.Application.Tests;

internal sealed class InMemoryTournaments : ITournamentRepository
{
    private readonly Dictionary<Guid, Tournament> _saved = [];
    private readonly List<Tournament> _pendingAdds = [];
    private readonly List<Tournament> _pendingRemovals = [];

    public int SaveCount { get; private set; }

    public bool FailNextSave { get; set; }

    public IReadOnlyCollection<Tournament> Saved => _saved.Values;

    public Task<Tournament?> FindAsync(Guid id, CancellationToken cancellationToken) =>
        Task.FromResult(_saved.GetValueOrDefault(id));

    public void Add(Tournament tournament) => _pendingAdds.Add(tournament);

    public void Remove(Tournament tournament) => _pendingRemovals.Add(tournament);

    public Task SaveChangesAsync(CancellationToken cancellationToken)
    {
        if (FailNextSave)
        {
            FailNextSave = false;
            throw new ConflictException("Simulated conflict.");
        }

        _pendingAdds.ForEach(t => _saved[t.Id] = t);
        _pendingRemovals.ForEach(t => _saved.Remove(t.Id));
        _pendingAdds.Clear();
        _pendingRemovals.Clear();
        SaveCount++;
        return Task.CompletedTask;
    }
}

internal sealed class FakeReferences : IReferenceData
{
    public HashSet<Guid> Organisations { get; } = [];

    public HashSet<Guid> Players { get; } = [];

    public HashSet<Guid> Tables { get; } = [];

    public HashSet<Guid> Venues { get; } = [];

    public Task<bool> OrganisationExistsAsync(Guid id, CancellationToken cancellationToken) => Task.FromResult(Organisations.Contains(id));

    public Task<bool> PlayerExistsAsync(Guid id, CancellationToken cancellationToken) => Task.FromResult(Players.Contains(id));

    public Task<bool> TableExistsAsync(Guid id, CancellationToken cancellationToken) => Task.FromResult(Tables.Contains(id));

    public Task<bool> VenueExistsAsync(Guid id, CancellationToken cancellationToken) => Task.FromResult(Venues.Contains(id));
}

internal sealed class FixedTime(DateTimeOffset now) : TimeProvider
{
    public DateTimeOffset Now { get; set; } = now;

    public override DateTimeOffset GetUtcNow() => Now;
}

internal sealed class RecordingNotifier : ILiveNotifier
{
    public List<IReadOnlyList<MatchDto>> MatchBatches { get; } = [];

    public List<TournamentStatus> Statuses { get; } = [];

    public Task MatchesChangedAsync(Guid tournamentId, IReadOnlyList<MatchDto> matches, CancellationToken cancellationToken)
    {
        MatchBatches.Add(matches);
        return Task.CompletedTask;
    }

    public Task TournamentChangedAsync(Guid tournamentId, TournamentStatus status, CancellationToken cancellationToken)
    {
        Statuses.Add(status);
        return Task.CompletedTask;
    }
}

internal sealed class InMemoryLeagues : ILeagueRepository
{
    private readonly Dictionary<Guid, League> _saved = [];
    private readonly List<League> _pending = [];

    public int SaveCount { get; private set; }

    public Task<League?> FindAsync(Guid leagueId, CancellationToken cancellationToken) =>
        Task.FromResult(_saved.GetValueOrDefault(leagueId));

    public Task<League?> FindBySeasonAsync(Guid seasonId, CancellationToken cancellationToken) =>
        Task.FromResult(_saved.Values.SingleOrDefault(l => l.Seasons.Any(s => s.Id == seasonId)));

    public void Add(League league) => _pending.Add(league);

    public Task SaveChangesAsync(CancellationToken cancellationToken)
    {
        _pending.ForEach(l => _saved[l.Id] = l);
        _pending.Clear();
        SaveCount++;
        return Task.CompletedTask;
    }
}

internal sealed class InMemoryRankings : IRankingRepository
{
    private readonly Dictionary<Guid, Ranking> _saved = [];
    private readonly List<Ranking> _pending = [];

    public IReadOnlyCollection<Ranking> Saved => _saved.Values;

    public Task<Ranking?> FindAsync(Guid id, CancellationToken cancellationToken) => Task.FromResult(_saved.GetValueOrDefault(id));

    public void Add(Ranking ranking) => _pending.Add(ranking);

    public Task SaveChangesAsync(CancellationToken cancellationToken)
    {
        _pending.ForEach(r => _saved[r.Id] = r);
        _pending.Clear();
        return Task.CompletedTask;
    }
}

/// <summary>Ratings kept in memory. Real repositories save with the tournament; here changes apply at once.</summary>
internal sealed class InMemoryRatings : IRatingRepository
{
    public List<Rating> Ratings { get; } = [];

    public List<RatingChange> Changes { get; } = [];

    /// <summary>What <see cref="ListRatedResultsAsync"/> returns.</summary>
    public List<RatedResult> History { get; } = [];

    public Rating? For(Guid playerId, Discipline discipline) =>
        Ratings.SingleOrDefault(r => r.PlayerId == playerId && r.Discipline == discipline);

    public Task<Rating?> FindAsync(Guid playerId, Discipline discipline, CancellationToken cancellationToken) =>
        Task.FromResult(For(playerId, discipline));

    public void Add(Rating rating) => Ratings.Add(rating);

    public void Add(RatingChange change) => Changes.Add(change);

    public Task<IReadOnlyList<RatedResult>> ListRatedResultsAsync(CancellationToken cancellationToken) =>
        Task.FromResult<IReadOnlyList<RatedResult>>([.. History]);

    public Task ReplaceAllAsync(IReadOnlyList<Rating> ratings, IReadOnlyList<RatingChange> changes, CancellationToken cancellationToken)
    {
        Ratings.Clear();
        Ratings.AddRange(ratings);
        Changes.Clear();
        Changes.AddRange(changes);
        return Task.CompletedTask;
    }
}
