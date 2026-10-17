using Rackline.Application.Common;
using Rackline.Application.Live;
using Rackline.Domain.Organisations;
using Rackline.Domain.Tournaments;

namespace Rackline.Application.Tournaments;

/// <summary>Registering players, seeding and handicaps.</summary>
public sealed class TournamentEntries(ITournamentRepository tournaments, IReferenceData references, OrganisationAccess access, ILiveNotifier notifier)
{
    public async Task<Guid> AddAsync(Guid tournamentId, Guid playerId, int? seed, int handicap, CancellationToken cancellationToken = default)
    {
        var tournament = await tournaments.GetAsync(tournamentId, cancellationToken);
        await access.EnsureAsync(tournament.OrganisationId, StaffRole.Organiser, cancellationToken);
        if (!await references.PlayerExistsAsync(playerId, cancellationToken))
        {
            throw new NotFoundException("Player", playerId);
        }

        var entry = tournament.AddEntry(playerId, seed, handicap);
        await tournaments.SaveChangesAsync(cancellationToken);
        await notifier.TournamentChangedAsync(tournamentId, tournament.Status, cancellationToken);
        return entry.Id;
    }

    public async Task UpdateAsync(Guid tournamentId, Guid entryId, int? seed, int handicap, CancellationToken cancellationToken = default)
    {
        var tournament = await GetWithEntryAsync(tournamentId, entryId, cancellationToken);
        tournament.SetSeed(entryId, seed);
        tournament.SetHandicap(entryId, handicap);
        await tournaments.SaveChangesAsync(cancellationToken);
        await notifier.TournamentChangedAsync(tournamentId, tournament.Status, cancellationToken);
    }

    public async Task WithdrawAsync(Guid tournamentId, Guid entryId, CancellationToken cancellationToken = default)
    {
        var tournament = await GetWithEntryAsync(tournamentId, entryId, cancellationToken);
        tournament.WithdrawEntry(entryId);
        await tournaments.SaveChangesAsync(cancellationToken);
        await notifier.TournamentChangedAsync(tournamentId, tournament.Status, cancellationToken);
    }

    private async Task<Tournament> GetWithEntryAsync(Guid tournamentId, Guid entryId, CancellationToken cancellationToken)
    {
        var tournament = await tournaments.GetAsync(tournamentId, cancellationToken);
        await access.EnsureAsync(tournament.OrganisationId, StaffRole.Organiser, cancellationToken);
        return tournament.Entries.Any(e => e.Id == entryId)
            ? tournament
            : throw new NotFoundException("Entry", entryId);
    }
}
