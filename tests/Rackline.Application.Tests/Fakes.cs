using Rackline.Application.Common;
using Rackline.Application.Live;
using Rackline.Application.Tournaments;
using Rackline.Domain.Leagues;
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
