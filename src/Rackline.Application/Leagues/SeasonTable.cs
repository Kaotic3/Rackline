using Rackline.Domain.Common;
using Rackline.Domain.Leagues;

namespace Rackline.Application.Leagues;

/// <summary>A season's league table from its DTO, using the domain's table rules.</summary>
public static class SeasonTable
{
    public static IReadOnlyList<LeagueTableRowDto> For(SeasonDetailDto season)
    {
        var r = season.Rules;
        var rules = new SeasonRules(
            r.RubbersPerFixture,
            r.RubberLengthKind == MatchLengthKind.RaceTo ? MatchLength.RaceTo(r.RubberLengthValue) : MatchLength.BestOf(r.RubberLengthValue),
            r.PointsForWin,
            r.PointsForDraw,
            r.PointsPerRubber);

        var outcomes = season.Fixtures
            .Where(f => f.Status == FixtureStatus.Completed)
            .Select(f => new FixtureOutcome(f.HomeTeamId, f.AwayTeamId, f.HomeRubbersWon, f.AwayRubbersWon));

        // Same team order as Season.Table(): alphabetical, so equal records are listed A–Z.
        var teams = season.Teams.OrderBy(t => t.Name, StringComparer.OrdinalIgnoreCase).Select(t => t.Id).ToList();

        return
        [
            .. LeagueTable.Calculate(teams, outcomes, rules).Select(row => new LeagueTableRowDto(
                row.Position, row.TeamId, season.TeamName(row.TeamId),
                row.Played, row.Won, row.Drawn, row.Lost, row.RubbersFor, row.RubbersAgainst, row.Points)),
        ];
    }
}
