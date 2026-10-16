using Rackline.Domain.Common;
using Rackline.Domain.Matches;

namespace Rackline.Domain.Draws;

public sealed class SingleEliminationEngine : IDrawEngine
{
    public IReadOnlyList<Match> Generate(Guid tournamentId, IReadOnlyList<Guid> seededEntries, MatchLength length)
    {
        DrawInput.Validate(seededEntries);

        var builder = new EliminationBuilder(tournamentId, length.FramesToWin);
        var rounds = builder.AddKnockout(BracketSide.Winners, SeedPlacement.BracketSize(seededEntries.Count));
        builder.PlaceSeeds(rounds[0], seededEntries);
        return builder.Matches;
    }
}
