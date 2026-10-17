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

/// <summary>One finished group match, reduced to what the table needs.</summary>
public sealed record GroupResult(Guid WinnerEntryId, Guid LoserEntryId, int WinnerFrames, int LoserFrames, bool Walkover)
{
    /// <summary>The result of a finished match between two entries; null for anything else.</summary>
    public static GroupResult? From(Match match)
    {
        if (!match.IsResolved || match.WinnerEntryId is not { } winner || match.LoserEntryId is not { } loser)
        {
            return null;
        }

        var walkover = match.State == MatchState.Walkover;
        var winnerIsSlot1 = match.Slot1.EntryId == winner;
        return new GroupResult(
            winner,
            loser,
            walkover ? 0 : winnerIsSlot1 ? match.Score1 : match.Score2,
            walkover ? 0 : winnerIsSlot1 ? match.Score2 : match.Score1,
            walkover);
    }

    public bool Involves(Guid entryId) => WinnerEntryId == entryId || LoserEntryId == entryId;
}

/// <summary>Round-robin group table.</summary>
public static class GroupStandings
{
    /// <inheritdoc cref="Calculate(IReadOnlyList{Guid}, IEnumerable{GroupResult})"/>
    public static IReadOnlyList<StandingRow> Calculate(IReadOnlyList<Guid> entriesInDrawOrder, IEnumerable<Match> matches) =>
        Calculate(entriesInDrawOrder, matches.Select(GroupResult.From).OfType<GroupResult>());

    /// <summary>
    /// Ranks by matches won; a two-way tie is broken by head-to-head, otherwise by frame difference,
    /// then frames won, then draw order. Walkovers count as a win with no frames.
    /// </summary>
    /// <param name="entriesInDrawOrder">Every entry in the group, in seed/draw order.</param>
    /// <param name="results">The group's finished matches.</param>
    public static IReadOnlyList<StandingRow> Calculate(IReadOnlyList<Guid> entriesInDrawOrder, IEnumerable<GroupResult> results)
    {
        var played = results.ToList();
        var tallies = entriesInDrawOrder.Select((id, index) => Tally(id, index, played)).ToList();

        var ordered = tallies
            .GroupBy(t => t.Won)
            .OrderByDescending(g => g.Key)
            .SelectMany(g => BreakTie([.. g], played));

        return [.. ordered.Select((t, i) => new StandingRow(i + 1, t.EntryId, t.Played, t.Won, t.Lost, t.For, t.Against))];
    }

    private static IEnumerable<EntryTally> BreakTie(List<EntryTally> tied, List<GroupResult> played)
    {
        if (tied.Count == 2)
        {
            var meeting = played.FirstOrDefault(r => r.Involves(tied[0].EntryId) && r.Involves(tied[1].EntryId));
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

    private static EntryTally Tally(Guid entryId, int drawIndex, List<GroupResult> played)
    {
        var tally = new EntryTally(entryId, drawIndex);
        foreach (var result in played.Where(r => r.Involves(entryId)))
        {
            tally.Played++;
            var won = result.WinnerEntryId == entryId;
            if (won)
            {
                tally.Won++;
            }
            else
            {
                tally.Lost++;
            }

            tally.For += won ? result.WinnerFrames : result.LoserFrames;
            tally.Against += won ? result.LoserFrames : result.WinnerFrames;
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
