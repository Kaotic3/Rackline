using Rackline.Domain.Common;
using Rackline.Domain.Draws;
using Rackline.Domain.Matches;

namespace Rackline.Domain.Tests;

internal static class TestSupport
{
    public static readonly MatchLength RaceTo5 = MatchLength.RaceTo(5);

    public static readonly DateTimeOffset At = new(2026, 1, 10, 19, 0, 0, TimeSpan.Zero);

    public static readonly Guid TournamentId = new("00000000-0000-0000-0000-0000000000aa");

    /// <summary>Deterministic entry ids; index 0 is seed 1.</summary>
    public static IReadOnlyList<Guid> Entries(int count) =>
        [.. Enumerable.Range(1, count).Select(i => new Guid(i, 0, 0, new byte[8]))];

    public static IEnumerable<object[]> FieldSizes(int from, int to) =>
        Enumerable.Range(from, to - from + 1).Select(n => new object[] { n });

    /// <summary>
    /// Plays every ready match, lowest number first, until none are left.
    /// <paramref name="slot1Wins"/> decides each result.
    /// </summary>
    public static void PlayOut(Bracket bracket, Func<Match, bool> slot1Wins)
    {
        while (bracket.Matches.Where(m => m.State == MatchState.Ready).MinBy(m => m.Number) is { } match)
        {
            var win = match.FramesToWin;
            if (slot1Wins(match))
            {
                bracket.RecordResult(match.Number, win, win - 1, At);
            }
            else
            {
                bracket.RecordResult(match.Number, 0, win, At);
            }
        }
    }

    /// <summary>Returns a decider under which the better seed (lower index) always wins.</summary>
    public static Func<Match, bool> BetterSeedWins(IReadOnlyList<Guid> seeded)
    {
        var index = seeded.Select((id, i) => (id, i)).ToDictionary(x => x.id, x => x.i);
        return m => index[m.Slot1.EntryId!.Value] < index[m.Slot2.EntryId!.Value];
    }

    public static Dictionary<Guid, int> LossCounts(IEnumerable<Match> matches) =>
        matches.Where(m => m.LoserEntryId is not null)
            .GroupBy(m => m.LoserEntryId!.Value)
            .ToDictionary(g => g.Key, g => g.Count());

    /// <summary>A structural fingerprint of a match, for determinism checks.</summary>
    public static string Describe(Match m) =>
        $"{m.Number}|{m.Side}|{m.Round}|{m.Position}|{m.Group}|{m.Slot1}|{m.Slot2}|{m.State}|{m.WinnerTo}|{m.LoserTo}|{m.ResetMatchNumber}";
}
