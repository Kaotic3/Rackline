using Rackline.Application.Common;
using Rackline.Domain.Tournaments;

namespace Rackline.Application.Tournaments;

/// <summary>Registering players, seeding and handicaps.</summary>
public sealed class TournamentEntries(ITournamentRepository tournaments, IReferenceData references)
{
    public async Task<Guid> AddAsync(Guid tournamentId, Guid playerId, int? seed, int handicap, CancellationToken cancellationToken = default)
    {
        var tournament = await tournaments.GetAsync(tournamentId, cancellationToken);
        if (!await references.PlayerExistsAsync(playerId, cancellationToken))
        {
            throw new NotFoundException("Player", playerId);
        }

        var entry = tournament.AddEntry(playerId, seed, handicap);
        await tournaments.SaveChangesAsync(cancellationToken);
        return entry.Id;
    }

    public async Task UpdateAsync(Guid tournamentId, Guid entryId, int? seed, int handicap, CancellationToken cancellationToken = default)
    {
        var tournament = await GetWithEntryAsync(tournamentId, entryId, cancellationToken);
        tournament.SetSeed(entryId, seed);
        tournament.SetHandicap(entryId, handicap);
        await tournaments.SaveChangesAsync(cancellationToken);
    }

    public async Task WithdrawAsync(Guid tournamentId, Guid entryId, CancellationToken cancellationToken = default)
    {
        var tournament = await GetWithEntryAsync(tournamentId, entryId, cancellationToken);
        tournament.WithdrawEntry(entryId);
        await tournaments.SaveChangesAsync(cancellationToken);
    }

    private async Task<Tournament> GetWithEntryAsync(Guid tournamentId, Guid entryId, CancellationToken cancellationToken)
    {
        var tournament = await tournaments.GetAsync(tournamentId, cancellationToken);
        return tournament.Entries.Any(e => e.Id == entryId)
            ? tournament
            : throw new NotFoundException("Entry", entryId);
    }
}
