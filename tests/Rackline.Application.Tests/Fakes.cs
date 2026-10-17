using Rackline.Application.Common;
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

    public Task<bool> OrganisationExistsAsync(Guid id, CancellationToken cancellationToken) => Task.FromResult(Organisations.Contains(id));

    public Task<bool> PlayerExistsAsync(Guid id, CancellationToken cancellationToken) => Task.FromResult(Players.Contains(id));

    public Task<bool> TableExistsAsync(Guid id, CancellationToken cancellationToken) => Task.FromResult(Tables.Contains(id));
}

internal sealed class FixedTime(DateTimeOffset now) : TimeProvider
{
    public DateTimeOffset Now { get; set; } = now;

    public override DateTimeOffset GetUtcNow() => Now;
}
