using Rackline.Domain.Common;
using Rackline.Domain.Matches;
using Rackline.Domain.Tournaments;

namespace Rackline.Domain.Standings;

/// <summary>Where an entry finished in a tournament. Entries that went out at the same stage share a position.</summary>
public sealed record Placing(Guid EntryId, int Position);

/// <summary>Final positions in a finished draw, e.g. for ranking points.</summary>
public static class FinishingPositions
{
    /// <summary>
    /// Knockouts: the champion is first, and everyone else is placed by the stage they went out at
    /// (later is better), so both losing semi-finalists are third. In double elimination that is the
    /// losers' bracket round or grand final of the second loss. Round robins: entries are placed by
    /// their position in their group, so with several groups every group winner is first.
    /// </summary>
    public static IReadOnlyList<Placing> Calculate(Tournament tournament)
    {
        Guard.That(tournament.Matches.Count > 0 && tournament.Bracket.IsFinished, "Positions are known only once every match is finished.");

        var scores = tournament.Format switch
        {
            TournamentFormat.SingleElimination or TournamentFormat.DoubleElimination => KnockoutScores(tournament),
            TournamentFormat.RoundRobin => GroupScores(tournament),
            _ => throw new DomainException($"Positions for the {tournament.Format} format are not supported yet."),
        };

        return
        [
            .. scores
                .Select(s => new Placing(s.Key, 1 + scores.Values.Count(other => other > s.Value)))
                .OrderBy(p => p.Position)
                .ThenBy(p => p.EntryId),
        ];
    }

    /// <summary>Higher is better. The champion beats everyone; others score the stage of the loss that put them out.</summary>
    private static Dictionary<Guid, int> KnockoutScores(Tournament tournament)
    {
        var champion = tournament.Bracket.ChampionEntryId;
        var scores = new Dictionary<Guid, int>();

        foreach (var entry in tournament.Entries)
        {
            if (entry.Id == champion)
            {
                scores[entry.Id] = int.MaxValue;
                continue;
            }

            // A loss that sends a player on to another match (the losers' bracket) does not put them out.
            var outAt = tournament.Matches
                .Where(m => m.LoserEntryId == entry.Id && m.LoserTo is null)
                .Select(Stage)
                .DefaultIfEmpty(0)
                .Max();
            Guard.That(outAt > 0, "Every entry but the champion must have been knocked out.");
            scores[entry.Id] = outAt;
        }

        return scores;
    }

    private static int Stage(Match match) => match.Side switch
    {
        BracketSide.Winners or BracketSide.Losers => match.Round,
        BracketSide.GrandFinal => 1000 + match.Round,
        _ => throw new DomainException("Group matches have no knockout stage."),
    };

    private static Dictionary<Guid, int> GroupScores(Tournament tournament) =>
        tournament.Matches
            .GroupBy(m => m.Group)
            .SelectMany(group => GroupStandings.Calculate(GroupMembers(tournament, group), group))
            .ToDictionary(row => row.EntryId, row => -row.Position);

    /// <summary>The draw order is not stored, so seeds (then entry ids) give the final tie-break.</summary>
    private static IReadOnlyList<Guid> GroupMembers(Tournament tournament, IEnumerable<Match> groupMatches)
    {
        var seeds = tournament.Entries.ToDictionary(e => e.Id, e => e.Seed);
        return
        [
            .. groupMatches
                .SelectMany(m => new[] { m.Slot1.EntryId, m.Slot2.EntryId })
                .OfType<Guid>()
                .Distinct()
                .OrderBy(id => seeds[id] ?? int.MaxValue)
                .ThenBy(id => id),
        ];
    }
}
