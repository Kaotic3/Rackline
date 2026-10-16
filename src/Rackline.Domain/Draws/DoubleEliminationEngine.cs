using Rackline.Domain.Common;
using Rackline.Domain.Matches;

namespace Rackline.Domain.Draws;

/// <summary>
/// Winners' and losers' brackets feeding a grand final. With <paramref name="grandFinalReset"/>, a second
/// grand final is played if the losers' bracket champion wins the first.
/// </summary>
public sealed class DoubleEliminationEngine(bool grandFinalReset) : IDrawEngine
{
    public bool GrandFinalReset { get; } = grandFinalReset;

    public IReadOnlyList<Match> Generate(Guid tournamentId, IReadOnlyList<Guid> seededEntries, MatchLength length)
    {
        DrawInput.Validate(seededEntries);

        var size = SeedPlacement.BracketSize(seededEntries.Count);
        var builder = new EliminationBuilder(tournamentId, length.FramesToWin);
        var winners = builder.AddKnockout(BracketSide.Winners, size);
        var losers = AddLosersRounds(builder, size);
        var grandFinal = builder.Add(BracketSide.GrandFinal, 1, 1);

        if (GrandFinalReset)
        {
            grandFinal.LinkReset(builder.Add(BracketSide.GrandFinal, 2, 1).Number);
        }

        winners[^1][0].LinkWinnerTo(grandFinal.Number, 1);
        LinkWinnersBracketLosers(winners, losers, grandFinal);
        LinkLosersBracket(losers, grandFinal);

        builder.PlaceSeeds(winners[0], seededEntries);
        return builder.Matches;
    }

    /// <summary>
    /// Losers' round 1 takes the losers of winners' round 1. After that, rounds alternate between
    /// "drop-in" rounds (survivors meet the losers of the next winners' round) and consolidation rounds.
    /// </summary>
    private static Match[][] AddLosersRounds(EliminationBuilder builder, int size)
    {
        var rounds = new List<Match[]>();
        var count = size / 4;
        var round = 1;

        while (count >= 1)
        {
            // Round 1 and every even round keep the same number of matches; odd rounds after 1 halve it.
            if (round > 1 && round % 2 == 1)
            {
                count /= 2;
                if (count < 1)
                {
                    break;
                }
            }

            var r = round;
            rounds.Add([.. Enumerable.Range(1, count).Select(p => builder.Add(BracketSide.Losers, r, p))]);
            round++;
        }

        return [.. rounds];
    }

    private static void LinkWinnersBracketLosers(Match[][] winners, Match[][] losers, Match grandFinal)
    {
        if (losers.Length == 0)
        {
            // Two-player bracket: the loser of the only winners' match goes straight to the grand final.
            winners[0][0].LinkLoserTo(grandFinal.Number, 2);
            return;
        }

        for (var i = 0; i < winners[0].Length; i++)
        {
            winners[0][i].LinkLoserTo(losers[0][i / 2].Number, i % 2 + 1);
        }

        for (var r = 1; r < winners.Length; r++)
        {
            // Winners' round r+1 drops into losers' round 2r. Alternate the order to delay rematches.
            var target = losers[2 * r - 1];
            var reverse = r % 2 == 1;
            for (var i = 0; i < winners[r].Length; i++)
            {
                var position = reverse ? target.Length - 1 - i : i;
                winners[r][i].LinkLoserTo(target[position].Number, 2);
            }
        }
    }

    private static void LinkLosersBracket(Match[][] losers, Match grandFinal)
    {
        for (var t = 0; t < losers.Length; t++)
        {
            var roundNumber = t + 1;
            if (t == losers.Length - 1)
            {
                losers[t][0].LinkWinnerTo(grandFinal.Number, 2);
            }
            else if (roundNumber % 2 == 1)
            {
                for (var i = 0; i < losers[t].Length; i++)
                {
                    losers[t][i].LinkWinnerTo(losers[t + 1][i].Number, 1);
                }
            }
            else
            {
                EliminationBuilder.LinkPairsInto(losers[t], losers[t + 1]);
            }
        }
    }
}
