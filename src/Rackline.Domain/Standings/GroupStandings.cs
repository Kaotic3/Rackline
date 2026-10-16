using Rackline.Domain.Matches;

namespace Rackline.Domain.Standings;

public sealed record StandingRow(
    int Position,
    Guid EntryId,
    int Played,
    int Won,
    int Lost,
    int FramesFor,
    int FramesAgainst)
{
    public int FrameDifference => FramesFor - FramesAgainst;
}

/// <summary>Round-robin group table.</summary>
public static class GroupStandings
{
    /// <summary>
    /// Ranks by matches won; a two-way tie is broken by head-to-head, otherwise by frame difference,
    /// then frames won, then draw order. Walkovers count as a win with no frames.
    /// </summary>
    /// <param name="entriesInDrawOrder">Every entry in the group, in seed/draw order.</param>
    /// <param name="matches">The group's matches; unresolved matches are ignored.</param>
    public static IReadOnlyList<StandingRow> Calculate(IReadOnlyList<Guid> entriesInDrawOrder, IEnumerable<Match> matches)
    {
        var played = matches.Where(m => m.IsResolved && m.WinnerEntryId is not null && m.LoserEntryId is not null).ToList();
        var tallies = entriesInDrawOrder.Select((id, index) => Tally(id, index, played)).ToList();

        var ordered = tallies
            .GroupBy(t => t.Won)
            .OrderByDescending(g => g.Key)
            .SelectMany(g => BreakTie([.. g], played));

        return [.. ordered.Select((t, i) => new StandingRow(i + 1, t.EntryId, t.Played, t.Won, t.Lost, t.For, t.Against))];
    }

    private static IEnumerable<EntryTally> BreakTie(List<EntryTally> tied, List<Match> played)
    {
        if (tied.Count == 2)
        {
            var meeting = played.FirstOrDefault(m => m.Involves(tied[0].EntryId) && m.Involves(tied[1].EntryId));
            if (meeting is not null)
            {
                return tied.OrderByDescending(t => t.EntryId == meeting.WinnerEntryId);
            }
        }

        return tied
            .OrderByDescending(t => t.For - t.Against)
            .ThenByDescending(t => t.For)
            .ThenBy(t => t.DrawIndex);
    }

    private static EntryTally Tally(Guid entryId, int drawIndex, List<Match> played)
    {
        var tally = new EntryTally(entryId, drawIndex);
        foreach (var match in played.Where(m => m.Involves(entryId)))
        {
            tally.Played++;
            if (match.WinnerEntryId == entryId)
            {
                tally.Won++;
            }
            else
            {
                tally.Lost++;
            }

            if (match.State == MatchState.Completed)
            {
                var isSlot1 = match.Slot1.EntryId == entryId;
                tally.For += isSlot1 ? match.Score1 : match.Score2;
                tally.Against += isSlot1 ? match.Score2 : match.Score1;
            }
        }

        return tally;
    }

    private sealed class EntryTally(Guid entryId, int drawIndex)
    {
        public Guid EntryId { get; } = entryId;

        public int DrawIndex { get; } = drawIndex;

        public int Played { get; set; }

        public int Won { get; set; }

        public int Lost { get; set; }

        public int For { get; set; }

        public int Against { get; set; }
    }
}
