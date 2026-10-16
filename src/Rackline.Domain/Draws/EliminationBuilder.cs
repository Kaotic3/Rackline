using Rackline.Domain.Matches;

namespace Rackline.Domain.Draws;

/// <summary>Shared construction steps for knockout brackets.</summary>
internal sealed class EliminationBuilder(Guid tournamentId, int framesToWin)
{
    private readonly List<Match> _matches = [];

    public IReadOnlyList<Match> Matches => _matches;

    public Match Add(BracketSide side, int round, int position)
    {
        var match = new Match(tournamentId, _matches.Count + 1, side, round, position, framesToWin);
        _matches.Add(match);
        return match;
    }

    /// <summary>
    /// Builds a knockout bracket of <paramref name="size"/> lines with winners linked forward.
    /// Returns rounds[r][p] (both zero-based).
    /// </summary>
    public Match[][] AddKnockout(BracketSide side, int size)
    {
        var rounds = new List<Match[]>();
        for (int round = 1, count = size / 2; count >= 1; round++, count /= 2)
        {
            var current = Enumerable.Range(1, count).Select(p => Add(side, round, p)).ToArray();
            if (rounds.Count > 0)
            {
                LinkPairsInto(rounds[^1], current);
            }

            rounds.Add(current);
        }

        return [.. rounds];
    }

    /// <summary>Winners of matches 2p-1 and 2p go to slots 1 and 2 of match p in the next round.</summary>
    public static void LinkPairsInto(Match[] from, Match[] to)
    {
        for (var i = 0; i < from.Length; i++)
        {
            from[i].LinkWinnerTo(to[i / 2].Number, i % 2 + 1);
        }
    }

    /// <summary>Places seeds into the first round using standard placement; missing seeds become byes.</summary>
    public void PlaceSeeds(Match[] firstRound, IReadOnlyList<Guid> seededEntries)
    {
        var bracket = new Bracket(_matches);
        var order = SeedPlacement.Order(firstRound.Length * 2);

        for (var line = 0; line < order.Count; line++)
        {
            var seed = order[line];
            var value = seed <= seededEntries.Count ? MatchSlot.For(seededEntries[seed - 1]) : MatchSlot.Bye;
            bracket.Place(firstRound[line / 2].Number, line % 2 + 1, value);
        }
    }
}
