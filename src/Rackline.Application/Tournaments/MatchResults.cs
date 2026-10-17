using Rackline.Application.Common;
using Rackline.Application.Live;
using Rackline.Domain.Tournaments;

namespace Rackline.Application.Tournaments;

/// <summary>Starting matches and recording how they finished.</summary>
public sealed class MatchResults(ITournamentRepository tournaments, IReferenceData references, ILiveNotifier notifier, TimeProvider time)
{
    public async Task StartAsync(Guid tournamentId, int matchNumber, Guid? tableId, CancellationToken cancellationToken = default)
    {
        if (tableId is { } table && !await references.TableExistsAsync(table, cancellationToken))
        {
            throw new NotFoundException("Table", table);
        }

        await ChangeAsync(tournamentId, matchNumber, t => t.StartMatch(matchNumber, time.GetUtcNow(), tableId), cancellationToken);
    }

    public Task RecordResultAsync(Guid tournamentId, int matchNumber, int score1, int score2, CancellationToken cancellationToken = default) =>
        ChangeAsync(tournamentId, matchNumber, t => t.RecordResult(matchNumber, score1, score2, time.GetUtcNow()), cancellationToken);

    public Task AwardWalkoverAsync(Guid tournamentId, int matchNumber, Guid winnerEntryId, CancellationToken cancellationToken = default) =>
        ChangeAsync(tournamentId, matchNumber, t => t.AwardWalkover(matchNumber, winnerEntryId, time.GetUtcNow()), cancellationToken);

    private async Task ChangeAsync(Guid tournamentId, int matchNumber, Action<Tournament> change, CancellationToken cancellationToken)
    {
        var tournament = await tournaments.GetWithMatchAsync(tournamentId, matchNumber, cancellationToken);
        var changes = MatchChanges.Capture(tournament);

        change(tournament);
        await tournaments.SaveChangesAsync(cancellationToken);
        await notifier.MatchesChangedAsync(tournamentId, changes.Since(tournament), cancellationToken);
    }
}
