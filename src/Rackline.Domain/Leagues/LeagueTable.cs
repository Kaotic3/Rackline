namespace Rackline.Domain.Leagues;

/// <summary>A played fixture, reduced to what the table needs.</summary>
public sealed record FixtureOutcome(Guid HomeTeamId, Guid AwayTeamId, int HomeRubbers, int AwayRubbers)
{
    public static FixtureOutcome From(Fixture fixture) =>
        new(fixture.HomeTeamId, fixture.AwayTeamId, fixture.HomeRubbersWon, fixture.AwayRubbersWon);
}

public sealed record LeagueTableRow(
    int Position,
    Guid TeamId,
    int Played,
    int Won,
    int Drawn,
    int Lost,
    int RubbersFor,
    int RubbersAgainst,
    int Points)
{
    public int RubberDifference => RubbersFor - RubbersAgainst;
}

public static class LeagueTable
{
    /// <summary>
    /// Points come from the season's rules (win, draw and per rubber won). Ties are broken by
    /// rubber difference, then rubbers won, then the order the teams are given in. Tied teams
    /// share a position.
    /// </summary>
    public static IReadOnlyList<LeagueTableRow> Calculate(IReadOnlyList<Guid> teamsInOrder, IEnumerable<FixtureOutcome> outcomes, SeasonRules rules)
    {
        var played = outcomes.ToList();
        var rows = teamsInOrder.Select((team, index) =>
        {
            int won = 0, drawn = 0, lost = 0, rubbersFor = 0, rubbersAgainst = 0;
            foreach (var o in played.Where(o => o.HomeTeamId == team || o.AwayTeamId == team))
            {
                var mine = o.HomeTeamId == team ? o.HomeRubbers : o.AwayRubbers;
                var theirs = o.HomeTeamId == team ? o.AwayRubbers : o.HomeRubbers;
                rubbersFor += mine;
                rubbersAgainst += theirs;
                if (mine > theirs)
                {
                    won++;
                }
                else if (mine == theirs)
                {
                    drawn++;
                }
                else
                {
                    lost++;
                }
            }

            var points = won * rules.PointsForWin + drawn * rules.PointsForDraw + rubbersFor * rules.PointsPerRubber;
            return (Index: index, Row: new LeagueTableRow(0, team, won + drawn + lost, won, drawn, lost, rubbersFor, rubbersAgainst, points));
        })
        .OrderByDescending(x => x.Row.Points)
        .ThenByDescending(x => x.Row.RubberDifference)
        .ThenByDescending(x => x.Row.RubbersFor)
        .ThenBy(x => x.Index)
        .Select(x => x.Row)
        .ToList();

        // Teams level on points, difference and rubbers won share a position.
        var ranked = new List<LeagueTableRow>(rows.Count);
        for (var i = 0; i < rows.Count; i++)
        {
            var level = i > 0 && SameRecord(rows[i], rows[i - 1]);
            ranked.Add(rows[i] with { Position = level ? ranked[i - 1].Position : i + 1 });
        }

        return ranked;
    }

    private static bool SameRecord(LeagueTableRow a, LeagueTableRow b) =>
        a.Points == b.Points && a.RubberDifference == b.RubberDifference && a.RubbersFor == b.RubbersFor;
}
