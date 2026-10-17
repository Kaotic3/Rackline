using Rackline.Domain.Common;

namespace Rackline.Domain.Leagues;

public sealed record ScheduledPairing(int Round, Guid HomeTeamId, Guid AwayTeamId);

/// <summary>Round-robin fixture lists for leagues. Pure and deterministic.</summary>
public static class LeagueSchedule
{
    /// <summary>
    /// Everyone plays everyone once per half (circle method), one fixture per team per round, and
    /// every team is at home in half its matches (to within one). With <paramref name="homeAndAway"/>, the
    /// second half repeats the first with venues swapped. An odd number of teams gives one team a
    /// week off each round.
    /// </summary>
    public static IReadOnlyList<ScheduledPairing> Generate(IReadOnlyList<Guid> teamIds, bool homeAndAway)
    {
        Guard.That(teamIds.Count >= 2, "A schedule needs at least two teams.");
        Guard.That(teamIds.Distinct().Count() == teamIds.Count, "Each team may appear only once.");

        var circle = teamIds.Select(t => (Guid?)t).ToList();
        if (circle.Count % 2 == 1)
        {
            circle.Add(null);
        }

        var n = circle.Count;
        var index = teamIds.Select((t, i) => (t, i)).ToDictionary(x => x.t, x => x.i);
        var venueCycle = teamIds.Count % 2 == 1 ? teamIds.Count : teamIds.Count + 1;
        var firstHalf = new List<ScheduledPairing>();
        for (var round = 0; round < n - 1; round++)
        {
            for (var i = 0; i < n / 2; i++)
            {
                if (circle[i] is not { } a || circle[n - 1 - i] is not { } b)
                {
                    continue;
                }

                firstHalf.Add(HostsAgainst(index[a], index[b], venueCycle)
                    ? new ScheduledPairing(round + 1, a, b)
                    : new ScheduledPairing(round + 1, b, a));
            }

            var last = circle[^1];
            circle.RemoveAt(n - 1);
            circle.Insert(1, last);
        }

        if (!homeAndAway)
        {
            return firstHalf;
        }

        var offset = n - 1;
        return [.. firstHalf, .. firstHalf.Select(p => new ScheduledPairing(p.Round + offset, p.AwayTeamId, p.HomeTeamId))];
    }

    /// <summary>
    /// Venue rule: with the teams numbered round a circle of odd size (adding an imaginary team
    /// when the count is even), a team is at home against the teams up to halfway round from it.
    /// In an odd circle that gives everyone exactly half their matches at home, so dropping the
    /// imaginary team leaves every real team within one of half.
    /// </summary>
    private static bool HostsAgainst(int a, int b, int cycle)
    {
        var distance = ((b - a) % cycle + cycle) % cycle;
        return distance <= cycle / 2;
    }
}
