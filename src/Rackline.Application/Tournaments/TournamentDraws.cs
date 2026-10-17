using Rackline.Application.Common;
using Rackline.Application.Live;
using Rackline.Domain.Common;
using Rackline.Domain.Organisations;

namespace Rackline.Application.Tournaments;

/// <summary>Making and undoing the draw.</summary>
public sealed class TournamentDraws(ITournamentRepository tournaments, OrganisationAccess access, ILiveNotifier notifier)
{
    /// <summary>
    /// Makes the draw. Passing the same <paramref name="randomSeed"/> again reproduces it exactly;
    /// when none is given one is chosen. Returns the seed used.
    /// </summary>
    public async Task<int> MakeAsync(Guid tournamentId, int? randomSeed, CancellationToken cancellationToken = default)
    {
        var tournament = await tournaments.GetAsync(tournamentId, cancellationToken);
        await access.EnsureAsync(tournament.OrganisationId, StaffRole.Organiser, cancellationToken);
        var seed = randomSeed ?? Random.Shared.Next();

        tournament.MakeDraw(new SeededRandom(seed));
        await tournaments.SaveChangesAsync(cancellationToken);
        await notifier.TournamentChangedAsync(tournamentId, tournament.Status, cancellationToken);
        return seed;
    }

    public async Task UndoAsync(Guid tournamentId, CancellationToken cancellationToken = default)
    {
        var tournament = await tournaments.GetAsync(tournamentId, cancellationToken);
        await access.EnsureAsync(tournament.OrganisationId, StaffRole.Organiser, cancellationToken);
        tournament.UndoDraw();
        await tournaments.SaveChangesAsync(cancellationToken);
        await notifier.TournamentChangedAsync(tournamentId, tournament.Status, cancellationToken);
    }
}
