using Rackline.Application.Common;
using Rackline.Domain.Tournaments;

namespace Rackline.Application.Tournaments;

/// <summary>Starting matches and recording how they finished.</summary>
public sealed class MatchResults(ITournamentRepository tournaments, IReferenceData references, TimeProvider time)
{
    public async Task StartAsync(Guid tournamentId, int matchNumber, Guid? tableId, CancellationToken cancellationToken = default)
    {
        var tournament = await GetWithMatchAsync(tournamentId, matchNumber, cancellationToken);
        if (tableId is { } table && !await references.TableExistsAsync(table, cancellationToken))
        {
            throw new NotFoundException("Table", table);
        }

        tournament.StartMatch(matchNumber, time.GetUtcNow(), tableId);
        await tournaments.SaveChangesAsync(cancellationToken);
    }

    public async Task RecordResultAsync(Guid tournamentId, int matchNumber, int score1, int score2, CancellationToken cancellationToken = default)
    {
        var tournament = await GetWithMatchAsync(tournamentId, matchNumber, cancellationToken);
        tournament.RecordResult(matchNumber, score1, score2, time.GetUtcNow());
        await tournaments.SaveChangesAsync(cancellationToken);
    }

    public async Task AwardWalkoverAsync(Guid tournamentId, int matchNumber, Guid winnerEntryId, CancellationToken cancellationToken = default)
    {
        var tournament = await GetWithMatchAsync(tournamentId, matchNumber, cancellationToken);
        tournament.AwardWalkover(matchNumber, winnerEntryId, time.GetUtcNow());
        await tournaments.SaveChangesAsync(cancellationToken);
    }

    private async Task<Tournament> GetWithMatchAsync(Guid tournamentId, int matchNumber, CancellationToken cancellationToken)
    {
        var tournament = await tournaments.GetAsync(tournamentId, cancellationToken);
        return tournament.Matches.Any(m => m.Number == matchNumber)
            ? tournament
            : throw new NotFoundException("Match", matchNumber);
    }
}
