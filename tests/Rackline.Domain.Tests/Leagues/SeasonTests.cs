using FluentAssertions;
using Rackline.Domain.Common;
using Rackline.Domain.Leagues;

namespace Rackline.Domain.Tests.Leagues;

public class SeasonTests
{
    private static readonly DateOnly Autumn = new(2026, 9, 1);
    private static readonly DateTimeOffset FirstNight = new(2026, 9, 8, 19, 30, 0, TimeSpan.Zero);
    private static readonly SeasonRules FourRubbersRaceToTwo = new(4, MatchLength.RaceTo(2));

    private static League NewLeague() => new(Guid.CreateVersion7(), "Tuesday Night League", Discipline.EnglishEightBall);

    private static Season NewSeason(SeasonRules? rules = null) =>
        NewLeague().AddSeason("Autumn 2026", Autumn, Autumn.AddMonths(4), rules ?? FourRubbersRaceToTwo);

    /// <summary>A season with two teams of four players each and its fixtures made.</summary>
    private static (Season Season, Team Home, Team Away, List<Guid> HomePlayers, List<Guid> AwayPlayers) ReadySeason()
    {
        var season = NewSeason();
        var home = season.AddTeam("Cushion Kings");
        var away = season.AddTeam("Pocket Rockets");
        var homePlayers = Enumerable.Range(0, 4).Select(_ => Guid.CreateVersion7()).ToList();
        var awayPlayers = Enumerable.Range(0, 4).Select(_ => Guid.CreateVersion7()).ToList();
        homePlayers.ForEach(p => season.AddPlayerToTeam(home.Id, p));
        awayPlayers.ForEach(p => season.AddPlayerToTeam(away.Id, p));
        season.GenerateFixtures(FirstNight);
        return (season, home, away, homePlayers, awayPlayers);
    }

    private static List<RubberResult> Rubbers(Fixture fixture, Team home, List<Guid> homePlayers, List<Guid> awayPlayers, params (int Home, int Away)[] scores)
    {
        var (hp, ap) = fixture.HomeTeamId == home.Id ? (homePlayers, awayPlayers) : (awayPlayers, homePlayers);
        return [.. scores.Select((s, i) => new RubberResult(hp[i], ap[i], s.Home, s.Away))];
    }

    [Fact]
    public void Seasons_cannot_overlap_or_run_backwards()
    {
        var league = NewLeague();
        league.AddSeason("Autumn 2026", Autumn, Autumn.AddMonths(3), FourRubbersRaceToTwo);

        league.Invoking(l => l.AddSeason("Clash", Autumn.AddMonths(2), Autumn.AddMonths(5), FourRubbersRaceToTwo)).Should().Throw<DomainException>();
        league.Invoking(l => l.AddSeason("Backwards", Autumn.AddMonths(6), Autumn.AddMonths(5), FourRubbersRaceToTwo)).Should().Throw<DomainException>();
        league.AddSeason("Spring 2027", Autumn.AddMonths(4), Autumn.AddMonths(7), FourRubbersRaceToTwo);
        league.Seasons.Should().HaveCount(2);
    }

    [Theory]
    [InlineData(0, 2, 1, 0)]
    [InlineData(31, 2, 1, 0)]
    [InlineData(4, 1, 2, 0)]
    [InlineData(4, 2, -1, 0)]
    [InlineData(4, 2, 1, -1)]
    public void Rules_must_make_sense(int rubbers, int win, int draw, int perRubber) =>
        FluentActions.Invoking(() => new SeasonRules(rubbers, MatchLength.RaceTo(2), win, draw, perRubber)).Should().Throw<DomainException>();

    [Fact]
    public void Team_names_are_unique_and_teams_are_fixed_once_fixtures_exist()
    {
        var season = NewSeason();
        var kings = season.AddTeam("Cushion Kings");
        season.Invoking(s => s.AddTeam("cushion kings")).Should().Throw<DomainException>();
        season.Invoking(s => s.GenerateFixtures(FirstNight)).Should().Throw<DomainException>("one team is not a league");

        var spare = season.AddTeam("Spare Team");
        season.RemoveTeam(spare.Id);
        season.AddTeam("Pocket Rockets");
        season.ChangeRules(new SeasonRules(6, MatchLength.RaceTo(1)));
        season.GenerateFixtures(FirstNight);

        season.Status.Should().Be(SeasonStatus.Scheduled);
        season.Invoking(s => s.AddTeam("Late Joiners")).Should().Throw<DomainException>();
        season.Invoking(s => s.RemoveTeam(kings.Id)).Should().Throw<DomainException>();
        season.Invoking(s => s.ChangeRules(FourRubbersRaceToTwo)).Should().Throw<DomainException>();
        season.Invoking(s => s.GenerateFixtures(FirstNight)).Should().Throw<DomainException>();
    }

    [Fact]
    public void Fixtures_are_weekly_and_must_fit_the_season()
    {
        var season = NewSeason();
        for (var i = 0; i < 4; i++)
        {
            season.AddTeam($"Team {i + 1}");
        }

        season.Invoking(s => s.GenerateFixtures(FirstNight.AddMonths(4))).Should().Throw<DomainException>("they would run past the end");
        season.Invoking(s => s.GenerateFixtures(FirstNight.AddMonths(-1))).Should().Throw<DomainException>("they would start too early");

        var fixtures = season.GenerateFixtures(FirstNight, daysBetweenRounds: 7, homeAndAway: true);

        fixtures.Should().HaveCount(12);
        fixtures.Select(f => (f.Round, f.ScheduledFor)).Distinct().Should().HaveCount(6);
        fixtures.Where(f => f.Round == 6).Should().OnlyContain(f => f.ScheduledFor == FirstNight.AddDays(35));
        fixtures.Should().OnlyContain(f => f.Status == FixtureStatus.Scheduled);
    }

    [Fact]
    public void A_player_can_play_for_only_one_team_in_a_season()
    {
        var season = NewSeason();
        var kings = season.AddTeam("Cushion Kings");
        var rockets = season.AddTeam("Pocket Rockets");
        var ada = Guid.CreateVersion7();

        season.AddPlayerToTeam(kings.Id, ada);
        season.AddPlayerToTeam(kings.Id, ada);
        kings.PlayerIds.Should().ContainSingle();
        season.Invoking(s => s.AddPlayerToTeam(rockets.Id, ada)).Should().Throw<DomainException>();

        season.RemovePlayerFromTeam(kings.Id, ada);
        season.AddPlayerToTeam(rockets.Id, ada);
        rockets.HasPlayer(ada).Should().BeTrue();
        season.Invoking(s => s.AddPlayerToTeam(Guid.CreateVersion7(), ada)).Should().Throw<DomainException>("no such team");
    }

    [Fact]
    public void A_result_records_every_rubber_and_completes_the_fixture()
    {
        var (season, home, _, hp, ap) = ReadySeason();
        var fixture = season.Fixtures.First();
        var at = FirstNight.AddHours(3);

        season.RecordResult(fixture.Id, Rubbers(fixture, home, hp, ap, (2, 1), (0, 2), (2, 0), (2, 1)), at);

        fixture.Should().BeEquivalentTo(new { Status = FixtureStatus.Completed, CompletedAt = at, HomeRubbersWon = 3, AwayRubbersWon = 1 }, o => o.ExcludingMissingMembers());
        fixture.Rubbers.Select(r => r.Number).Should().Equal(1, 2, 3, 4);
    }

    [Fact]
    public void Results_can_be_corrected_by_entering_them_again()
    {
        var (season, home, _, hp, ap) = ReadySeason();
        var fixture = season.Fixtures.First();
        season.RecordResult(fixture.Id, Rubbers(fixture, home, hp, ap, (2, 1), (2, 1), (2, 1), (2, 1)), FirstNight);

        season.RecordResult(fixture.Id, Rubbers(fixture, home, hp, ap, (0, 2), (0, 2), (2, 1), (2, 1)), FirstNight.AddDays(1));

        fixture.Rubbers.Should().HaveCount(4);
        (fixture.HomeRubbersWon, fixture.AwayRubbersWon).Should().Be((2, 2));
    }

    [Fact]
    public void Invalid_results_are_refused_and_change_nothing()
    {
        var (season, home, _, hp, ap) = ReadySeason();
        var fixture = season.Fixtures.First();
        var outsider = Guid.CreateVersion7();
        var good = Rubbers(fixture, home, hp, ap, (2, 1), (2, 1), (2, 1), (2, 1));

        season.Invoking(s => s.RecordResult(fixture.Id, good.Take(3).ToList(), FirstNight)).Should().Throw<DomainException>("four rubbers are needed");
        season.Invoking(s => s.RecordResult(fixture.Id, [.. good.Take(3), good[3] with { HomeScore = 2, AwayScore = 2 }], FirstNight))
            .Should().Throw<DomainException>("one player must win");
        season.Invoking(s => s.RecordResult(fixture.Id, [.. good.Take(3), good[3] with { HomeScore = 1, AwayScore = 0 }], FirstNight))
            .Should().Throw<DomainException>("nobody reached two");
        season.Invoking(s => s.RecordResult(fixture.Id, [.. good.Take(3), good[3] with { HomePlayerId = outsider }], FirstNight))
            .Should().Throw<DomainException>("the player is not on the team");
        season.Invoking(s => s.RecordResult(fixture.Id, [.. good.Take(3), good[3] with { HomePlayerId = good[3].AwayPlayerId, AwayPlayerId = good[3].HomePlayerId }], FirstNight))
            .Should().Throw<DomainException>("players are on the wrong sides");
        season.Invoking(s => s.RecordResult(Guid.CreateVersion7(), good, FirstNight)).Should().Throw<DomainException>("no such fixture");

        fixture.Status.Should().Be(FixtureStatus.Scheduled);
        fixture.Rubbers.Should().BeEmpty();
    }

    [Fact]
    public void Fixtures_can_be_postponed_and_rescheduled_until_played()
    {
        var (season, home, _, hp, ap) = ReadySeason();
        var fixture = season.Fixtures.First();

        season.Reschedule(fixture.Id, null);
        fixture.Should().BeEquivalentTo(new { Status = FixtureStatus.Postponed, ScheduledFor = (DateTimeOffset?)null }, o => o.ExcludingMissingMembers());

        season.Reschedule(fixture.Id, FirstNight.AddDays(3));
        fixture.Should().BeEquivalentTo(new { Status = FixtureStatus.Scheduled, ScheduledFor = FirstNight.AddDays(3) }, o => o.ExcludingMissingMembers());

        season.RecordResult(fixture.Id, Rubbers(fixture, home, hp, ap, (2, 0), (2, 0), (2, 0), (2, 0)), FirstNight);
        season.Invoking(s => s.Reschedule(fixture.Id, FirstNight)).Should().Throw<DomainException>();
    }

    [Fact]
    public void A_season_completes_only_when_every_fixture_is_played()
    {
        var (season, home, _, hp, ap) = ReadySeason();
        var first = season.Fixtures[0];
        season.RecordResult(first.Id, Rubbers(first, home, hp, ap, (2, 0), (2, 0), (2, 0), (2, 0)), FirstNight);
        season.Invoking(s => s.Complete()).Should().Throw<DomainException>();

        var second = season.Fixtures[1];
        season.RecordResult(second.Id, Rubbers(second, home, hp, ap, (0, 2), (0, 2), (2, 0), (0, 2)), FirstNight.AddDays(7));
        season.Complete();

        season.Status.Should().Be(SeasonStatus.Completed);
        season.Invoking(s => s.RecordResult(first.Id, Rubbers(first, home, hp, ap, (2, 0), (2, 0), (2, 0), (2, 0)), FirstNight)).Should().Throw<DomainException>();
        season.Invoking(s => s.AddPlayerToTeam(home.Id, Guid.CreateVersion7())).Should().Throw<DomainException>();
    }

    [Fact]
    public void The_table_reflects_the_results()
    {
        var (season, home, away, hp, ap) = ReadySeason();
        var first = season.Fixtures[0];
        season.RecordResult(first.Id, Rubbers(first, home, hp, ap, (2, 0), (2, 0), (2, 0), (0, 2)), FirstNight);

        var table = season.Table();

        table.Select(r => r.TeamId).Should().Equal(home.Id, away.Id);
        table[0].Should().BeEquivalentTo(new { Position = 1, Played = 1, Won = 1, RubbersFor = 3, RubbersAgainst = 1, Points = 2 });
        table[1].Should().BeEquivalentTo(new { Position = 2, Played = 1, Lost = 1, Points = 0 });
    }
}
