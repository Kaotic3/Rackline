using FluentAssertions;
using Rackline.Domain.Common;
using Rackline.Domain.Leagues;
using static Rackline.Domain.Tests.TestSupport;

namespace Rackline.Domain.Tests.Leagues;

public class LeagueScheduleTests
{
    public static IEnumerable<object[]> TeamCounts => FieldSizes(2, 20);

    [Theory]
    [MemberData(nameof(TeamCounts))]
    public void Home_and_away_has_every_team_host_every_other_once(int n)
    {
        var teams = Entries(n);

        var schedule = LeagueSchedule.Generate(teams, homeAndAway: true);

        schedule.Should().HaveCount(n * (n - 1));
        schedule.Select(p => (p.HomeTeamId, p.AwayTeamId)).Should().OnlyHaveUniqueItems();
        schedule.Should().OnlyContain(p => p.HomeTeamId != p.AwayTeamId);
        foreach (var team in teams)
        {
            schedule.Count(p => p.HomeTeamId == team).Should().Be(n - 1);
            schedule.Count(p => p.AwayTeamId == team).Should().Be(n - 1);
        }
    }

    [Theory]
    [MemberData(nameof(TeamCounts))]
    public void A_single_round_robin_meets_once_with_balanced_venues(int n)
    {
        var teams = Entries(n);

        var schedule = LeagueSchedule.Generate(teams, homeAndAway: false);

        schedule.Should().HaveCount(n * (n - 1) / 2);
        schedule.Select(p => p.HomeTeamId.CompareTo(p.AwayTeamId) < 0 ? (p.HomeTeamId, p.AwayTeamId) : (p.AwayTeamId, p.HomeTeamId))
            .Should().OnlyHaveUniqueItems();
        foreach (var team in teams)
        {
            var home = schedule.Count(p => p.HomeTeamId == team);
            var away = schedule.Count(p => p.AwayTeamId == team);
            Math.Abs(home - away).Should().BeLessThanOrEqualTo(1, "venues should be shared out fairly");
        }
    }

    [Theory]
    [MemberData(nameof(TeamCounts))]
    public void Nobody_plays_twice_in_a_round(int n)
    {
        var schedule = LeagueSchedule.Generate(Entries(n), homeAndAway: true);
        var roundsPerHalf = n % 2 == 0 ? n - 1 : n;

        schedule.Select(p => p.Round).Distinct().Should().HaveCount(2 * roundsPerHalf);
        foreach (var round in schedule.GroupBy(p => p.Round))
        {
            round.SelectMany(p => new[] { p.HomeTeamId, p.AwayTeamId }).Should().OnlyHaveUniqueItems();
            round.Should().HaveCount(n / 2, "with an odd number, one team has the week off");
        }
    }

    [Fact]
    public void The_second_half_mirrors_the_first()
    {
        var schedule = LeagueSchedule.Generate(Entries(4), homeAndAway: true);

        var first = schedule.Where(p => p.Round <= 3).ToList();
        var second = schedule.Where(p => p.Round > 3).ToList();

        second.Select(p => (p.Round - 3, p.AwayTeamId, p.HomeTeamId))
            .Should().Equal(first.Select(p => (p.Round, p.HomeTeamId, p.AwayTeamId)));
    }

    [Fact]
    public void Generation_is_deterministic() =>
        LeagueSchedule.Generate(Entries(9), true).Should().Equal(LeagueSchedule.Generate(Entries(9), true));

    [Fact]
    public void Rejects_too_few_or_repeated_teams()
    {
        FluentActions.Invoking(() => LeagueSchedule.Generate(Entries(1), true)).Should().Throw<DomainException>();
        var one = Entries(1)[0];
        FluentActions.Invoking(() => LeagueSchedule.Generate([one, one], true)).Should().Throw<DomainException>();
    }
}
