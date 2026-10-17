using Rackline.Application.Common;
using Rackline.Domain.Common;

namespace Rackline.Application.Tournaments;

/// <summary>Making and undoing the draw.</summary>
public sealed class TournamentDraws(ITournamentRepository tournaments)
{
    /// <summary>
    /// Makes the draw. Passing the same <paramref name="randomSeed"/> again reproduces it exactly;
    /// when none is given one is chosen. Returns the seed used.
    /// </summary>
    public async Task<int> MakeAsync(Guid tournamentId, int? randomSeed, CancellationToken cancellationToken = default)
    {
        var tournament = await tournaments.GetAsync(tournamentId, cancellationToken);
        var seed = randomSeed ?? Random.Shared.Next();

        tournament.MakeDraw(new SeededRandom(seed));
        await tournaments.SaveChangesAsync(cancellationToken);
        return seed;
    }

    public async Task UndoAsync(Guid tournamentId, CancellationToken cancellationToken = default)
    {
        var tournament = await tournaments.GetAsync(tournamentId, cancellationToken);
        tournament.UndoDraw();
        await tournaments.SaveChangesAsync(cancellationToken);
    }
}
