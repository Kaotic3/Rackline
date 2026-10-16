using Rackline.Domain.Common;
using Rackline.Domain.Matches;

namespace Rackline.Domain.Draws;

/// <summary>
/// Everyone plays everyone else in their group once. Entries are spread across groups in a
/// serpentine ("snake") pattern by seed, so group strengths stay balanced.
/// </summary>
public sealed class RoundRobinEngine : IDrawEngine
{
    public RoundRobinEngine(int groupCount = 1)
    {
        GroupCount = Guard.Positive(groupCount, "Group count");
    }

    public int GroupCount { get; }

    public IReadOnlyList<Match> Generate(Guid tournamentId, IReadOnlyList<Guid> seededEntries, MatchLength length)
    {
        DrawInput.Validate(seededEntries);
        Guard.That(seededEntries.Count >= GroupCount * 2, "Every group needs at least two entries.");

        var groups = AssignGroups(seededEntries, GroupCount);
        var schedules = groups.Select(Schedule).ToList();
        var roundCount = schedules.Max(s => s.Count);
        var matches = new List<Match>();

        // Number matches round by round across groups, which is the natural order to play them in.
        for (var round = 0; round < roundCount; round++)
        {
            for (var g = 0; g < schedules.Count; g++)
            {
                if (round >= schedules[g].Count)
                {
                    continue;
                }

                var position = 1;
                foreach (var (a, b) in schedules[g][round])
                {
                    var match = new Match(tournamentId, matches.Count + 1, BracketSide.Group, round + 1, position++, length.FramesToWin, g + 1);
                    match.Place(1, MatchSlot.For(a));
                    match.Place(2, MatchSlot.For(b));
                    matches.Add(match);
                }
            }
        }

        return matches;
    }

    /// <summary>Splits entries into groups by snake seeding: 1→A, 2→B, 3→B, 4→A, …</summary>
    public static IReadOnlyList<IReadOnlyList<Guid>> AssignGroups(IReadOnlyList<Guid> seededEntries, int groupCount)
    {
        var groups = Enumerable.Range(0, groupCount).Select(_ => new List<Guid>()).ToArray();
        for (var k = 0; k < seededEntries.Count; k++)
        {
            var row = k / groupCount;
            var column = k % groupCount;
            groups[row % 2 == 0 ? column : groupCount - 1 - column].Add(seededEntries[k]);
        }

        return groups;
    }

    /// <summary>Circle method: fix the first entry and rotate the rest. Odd fields get a sit-out each round.</summary>
    private static List<List<(Guid, Guid)>> Schedule(IReadOnlyList<Guid> entries)
    {
        var circle = entries.Select(e => (Guid?)e).ToList();
        if (circle.Count % 2 == 1)
        {
            circle.Add(null);
        }

        var n = circle.Count;
        var rounds = new List<List<(Guid, Guid)>>();

        for (var round = 0; round < n - 1; round++)
        {
            var pairs = new List<(Guid, Guid)>();
            for (var i = 0; i < n / 2; i++)
            {
                if (circle[i] is { } a && circle[n - 1 - i] is { } b)
                {
                    // Alternate who is listed first for the fixed entry, so nobody is always slot 1.
                    pairs.Add(i == 0 && round % 2 == 1 ? (b, a) : (a, b));
                }
            }

            rounds.Add(pairs);

            var last = circle[^1];
            circle.RemoveAt(n - 1);
            circle.Insert(1, last);
        }

        return rounds;
    }
}
