using Rackline.Domain.Common;
using Rackline.Domain.Matches;

namespace Rackline.Domain.Draws;

/// <summary>Builds the full set of matches for a tournament format. Must be pure and deterministic.</summary>
public interface IDrawEngine
{
    /// <param name="tournamentId">Tournament the matches belong to.</param>
    /// <param name="seededEntries">Entry ids in seed order: index 0 is seed 1.</param>
    /// <param name="length">Length of every match.</param>
    IReadOnlyList<Match> Generate(Guid tournamentId, IReadOnlyList<Guid> seededEntries, MatchLength length);
}

internal static class DrawInput
{
    public static void Validate(IReadOnlyList<Guid> seededEntries)
    {
        Guard.That(seededEntries.Count >= 2, "At least two entries are needed for a draw.");
        Guard.That(!seededEntries.Contains(Guid.Empty), "Entry ids must not be empty.");
        Guard.That(seededEntries.Distinct().Count() == seededEntries.Count, "Each entry may appear only once in a draw.");
    }
}
