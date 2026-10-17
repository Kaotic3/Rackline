using Rackline.Application.Common;
using Rackline.Application.Live;
using Rackline.Application.Rankings;

namespace Rackline.Application.Tournaments;

/// <summary>
/// Frame-by-frame scoring from the table, broadcast to live viewers. The deciding frame completes the
/// match, which is rated in the same save.
/// </summary>
public sealed class LiveScoring(ITournamentRepository tournaments, IRatingRepository ratings, ILiveNotifier notifier, TimeProvider time)
{
    public async Task RecordFrameAsync(
        Guid tournamentId,
        int matchNumber,
        int winnerSlot,
        bool breakAndRun,
        int? points1,
        int? points2,
        CancellationToken cancellationToken = default)
    {
        var tournament = await tournaments.GetWithMatchAsync(tournamentId, matchNumber, cancellationToken);
        var changes = MatchChanges.Capture(tournament);
        var rated = RatingUpdates.RatedSoFar(tournament);

        tournament.RecordFrame(matchNumber, winnerSlot, time.GetUtcNow(), breakAndRun, points1, points2);
        await new RatingUpdates(ratings).RateNewResultsAsync(tournament, rated, cancellationToken);
        await tournaments.SaveChangesAsync(cancellationToken);
        await notifier.MatchesChangedAsync(tournamentId, changes.Since(tournament), cancellationToken);
    }

    public async Task UndoLastFrameAsync(Guid tournamentId, int matchNumber, CancellationToken cancellationToken = default)
    {
        var tournament = await tournaments.GetWithMatchAsync(tournamentId, matchNumber, cancellationToken);
        var changes = MatchChanges.Capture(tournament);

        tournament.UndoLastFrame(matchNumber);
        await tournaments.SaveChangesAsync(cancellationToken);
        await notifier.MatchesChangedAsync(tournamentId, changes.Since(tournament), cancellationToken);
    }
}
