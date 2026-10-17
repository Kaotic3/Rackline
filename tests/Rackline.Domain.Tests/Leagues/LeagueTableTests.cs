using FluentAssertions;
using Rackline.Domain.Common;
using Rackline.Domain.Leagues;
using static Rackline.Domain.Tests.TestSupport;

namespace Rackline.Domain.Tests.Leagues;

public class LeagueTableTests
{
    private static readonly IReadOnlyList<Guid> Teams = Entries(4);
    private static Guid A => Teams[0];
    private static Guid B => Teams[1];
    private static Guid C => Teams[2];
    private static Guid D => Teams[3];

    [Fact]
    public void Wins_and_draws_score_by_the_rules_and_ties_go_to_rubber_difference()
    {
        var rules = new SeasonRules(4, MatchLength.RaceTo(1), pointsForWin: 3, pointsForDraw: 1);

        var table = LeagueTable.Calculate(Teams,
        [
            new FixtureOutcome(A, B, 3, 1),
            new FixtureOutcome(C, D, 4, 0),
            new FixtureOutcome(B, C, 2, 2),
            new FixtureOutcome(D, A, 2, 2),
        ], rules);

        table.Select(r => r.TeamId).Should().Equal(C, A, B, D);
        table.Select(r => r.Points).Should().Equal(4, 4, 1, 1);
        table[0].Should().BeEquivalentTo(new { Played = 2, Won = 1, Drawn = 1, Lost = 0, RubbersFor = 6, RubbersAgainst = 2 });
        table[0].RubberDifference.Should().Be(4);
    }

    [Fact]
    public void Points_per_rubber_are_added_when_the_rules_say_so()
    {
        var rules = new SeasonRules(5, MatchLength.RaceTo(1), pointsForWin: 2, pointsForDraw: 1, pointsPerRubber: 1);

        var table = LeagueTable.Calculate([A, B], [new FixtureOutcome(A, B, 3, 2)], rules);

        table.Single(r => r.TeamId == A).Points.Should().Be(2 + 3);
        table.Single(r => r.TeamId == B).Points.Should().Be(0 + 2);
    }

    [Fact]
    public void Teams_level_on_everything_share_a_position_in_the_given_order()
    {
        var rules = new SeasonRules(2, MatchLength.RaceTo(1));

        var table = LeagueTable.Calculate(Teams, [new FixtureOutcome(A, B, 1, 1), new FixtureOutcome(C, D, 1, 1)], rules);

        table.Select(r => r.Position).Should().Equal(1, 1, 1, 1);
        table.Select(r => r.TeamId).Should().Equal(Teams);
    }

    [Fact]
    public void Before_any_results_everyone_is_level()
    {
        var table = LeagueTable.Calculate(Teams, [], new SeasonRules(4, MatchLength.RaceTo(1)));

        table.Should().OnlyContain(r => r.Played == 0 && r.Points == 0 && r.Position == 1);
    }
}
