using FluentAssertions;
using Rackline.Application.Common;
using Rackline.Application.Leagues;
using Rackline.Domain.Common;
using Rackline.Domain.Leagues;

namespace Rackline.Application.Tests;

public class LeagueUseCaseTests
{
    private static readonly DateTimeOffset FirstNight = new(2026, 9, 8, 19, 30, 0, TimeSpan.Zero);
    private static readonly SeasonRulesInput TwoRubbers = new(2, MatchLengthKind.RaceTo, 1, PointsForWin: 3);

    private readonly InMemoryLeagues _leagues = new();
    private readonly FakeReferences _references = new();
    private readonly FixedTime _time = new(FirstNight.AddHours(3));
    private readonly LeagueSetup _setup;
    private readonly LeagueResults _results;
    private readonly Guid _organisationId = Guid.CreateVersion7();

    public LeagueUseCaseTests()
    {
        _references.Organisations.Add(_organisationId);
        _setup = new LeagueSetup(_leagues, _references, Access.Admin());
        _results = new LeagueResults(_leagues, Access.Admin(), _time);
    }

    private Guid NewPlayer()
    {
        var id = Guid.CreateVersion7();
        _references.Players.Add(id);
        return id;
    }

    /// <summary>A season with two teams of two players and its fixtures made.</summary>
    private async Task<(Guid SeasonId, Season Season, Guid[] HomeTeamPlayers, Guid[] AwayTeamPlayers)> ReadySeasonAsync()
    {
        var leagueId = await _setup.CreateLeagueAsync(_organisationId, "Thursday League", Discipline.NineBall);
        var seasonId = await _setup.AddSeasonAsync(leagueId, "Autumn", new DateOnly(2026, 9, 1), new DateOnly(2026, 12, 1), TwoRubbers);
        var a = await _setup.AddTeamAsync(seasonId, "Alpha", null);
        var b = await _setup.AddTeamAsync(seasonId, "Bravo", null);
        Guid[] alpha = [NewPlayer(), NewPlayer()];
        Guid[] bravo = [NewPlayer(), NewPlayer()];
        foreach (var p in alpha)
        {
            await _setup.AddPlayerAsync(seasonId, a, p);
        }

        foreach (var p in bravo)
        {
            await _setup.AddPlayerAsync(seasonId, b, p);
        }

        await _setup.GenerateFixturesAsync(seasonId, FirstNight, 7, homeAndAway: true);
        var season = (await _leagues.FindBySeasonAsync(seasonId, default))!.FindSeason(seasonId);
        var first = season.Fixtures.Single(f => f.Round == 1);
        return first.HomeTeamId == a ? (seasonId, season, alpha, bravo) : (seasonId, season, bravo, alpha);
    }

    [Fact]
    public async Task Setting_up_a_season_saves_teams_rosters_and_fixtures()
    {
        var (_, season, home, away) = await ReadySeasonAsync();

        season.Status.Should().Be(SeasonStatus.Scheduled);
        season.Rules.Should().BeEquivalentTo(new { RubbersPerFixture = 2, PointsForWin = 3, RubberLength = MatchLength.RaceTo(1) });
        season.Teams.SelectMany(t => t.PlayerIds).Should().BeEquivalentTo([.. home, .. away]);
        season.Fixtures.Should().HaveCount(2);
    }

    [Fact]
    public async Task Unknown_things_are_not_found()
    {
        (await FluentActions.Awaiting(() => _setup.CreateLeagueAsync(Guid.CreateVersion7(), "X", Discipline.NineBall))
            .Should().ThrowAsync<NotFoundException>()).Which.Resource.Should().Be("Organisation");
        (await FluentActions.Awaiting(() => _setup.AddSeasonAsync(Guid.CreateVersion7(), "S", new DateOnly(2026, 1, 1), new DateOnly(2026, 2, 1), TwoRubbers))
            .Should().ThrowAsync<NotFoundException>()).Which.Resource.Should().Be("League");
        (await FluentActions.Awaiting(() => _setup.AddTeamAsync(Guid.CreateVersion7(), "T", null))
            .Should().ThrowAsync<NotFoundException>()).Which.Resource.Should().Be("Season");

        var (seasonId, season, _, _) = await ReadySeasonAsync();
        (await FluentActions.Awaiting(() => _setup.AddPlayerAsync(seasonId, Guid.CreateVersion7(), NewPlayer()))
            .Should().ThrowAsync<NotFoundException>()).Which.Resource.Should().Be("Team");
        (await FluentActions.Awaiting(() => _setup.AddPlayerAsync(seasonId, season.Teams[0].Id, Guid.CreateVersion7()))
            .Should().ThrowAsync<NotFoundException>()).Which.Resource.Should().Be("Player");
        (await FluentActions.Awaiting(() => _results.RescheduleAsync(seasonId, Guid.CreateVersion7(), null))
            .Should().ThrowAsync<NotFoundException>()).Which.Resource.Should().Be("Fixture");
        (await FluentActions.Awaiting(() => _setup.AddTeamAsync(seasonId, "T", Guid.CreateVersion7()))
            .Should().ThrowAsync<NotFoundException>()).Which.Resource.Should().Be("Venue");
    }

    [Fact]
    public async Task Results_use_the_clock_and_rules_breaches_surface()
    {
        var (seasonId, season, home, away) = await ReadySeasonAsync();
        var fixture = season.Fixtures.Single(f => f.Round == 1);

        await _results.RecordResultAsync(seasonId, fixture.Id, [new(home[0], away[0], 1, 0), new(home[1], away[1], 0, 1)]);

        fixture.Should().BeEquivalentTo(new { Status = FixtureStatus.Completed, CompletedAt = FirstNight.AddHours(3) }, o => o.ExcludingMissingMembers());
        await FluentActions.Awaiting(() => _results.RecordResultAsync(seasonId, fixture.Id, [new(home[0], away[0], 1, 0)]))
            .Should().ThrowAsync<DomainException>("two rubbers are needed");
    }

    [Fact]
    public async Task Fixtures_can_be_postponed_and_the_season_completed()
    {
        var (seasonId, season, home, away) = await ReadySeasonAsync();
        var second = season.Fixtures.Single(f => f.Round == 2);

        await _results.RescheduleAsync(seasonId, second.Id, null);
        second.Status.Should().Be(FixtureStatus.Postponed);

        foreach (var fixture in season.Fixtures)
        {
            var (h, a) = fixture.Round == 1 ? (home, away) : (away, home);
            await _results.RecordResultAsync(seasonId, fixture.Id, [new(h[0], a[0], 1, 0), new(h[1], a[1], 1, 0)]);
        }

        await _setup.CompleteSeasonAsync(seasonId);
        season.Status.Should().Be(SeasonStatus.Completed);
    }

    [Fact]
    public async Task The_season_table_dto_matches_the_domain_table()
    {
        var (seasonId, season, home, away) = await ReadySeasonAsync();
        var first = season.Fixtures.Single(f => f.Round == 1);
        await _results.RecordResultAsync(seasonId, first.Id, [new(home[0], away[0], 1, 0), new(home[1], away[1], 1, 0)]);

        var dto = new SeasonDetailDto(
            season.Id, Guid.CreateVersion7(), "Thursday League", Discipline.NineBall, season.Name, season.StartsOn, season.EndsOn, season.Status,
            new SeasonRulesDto(2, MatchLengthKind.RaceTo, 1, 3, 1, 0),
            [.. season.Teams.Select(t => new TeamDto(t.Id, t.Name, null, null, [.. t.PlayerIds.Select(p => new RosterPlayerDto(p, "Someone"))]))],
            [.. season.Fixtures.Select(f => new FixtureDto(f.Id, f.Round, f.HomeTeamId, f.AwayTeamId, f.ScheduledFor, f.Status, f.CompletedAt,
                [.. f.Rubbers.Select(r => new RubberDto(r.Number, r.HomePlayerId, r.AwayPlayerId, r.HomeScore, r.AwayScore))]))]);

        var table = SeasonTable.For(dto);

        table.Select(r => (r.Position, r.TeamId, r.Points, r.RubbersFor))
            .Should().Equal(season.Table().Select(r => (r.Position, r.TeamId, r.Points, r.RubbersFor)));
        table[0].Should().BeEquivalentTo(new { Points = 3, Won = 1, RubberDifference = 2 }, o => o.ExcludingMissingMembers());
        table[0].TeamName.Should().Be(season.FindTeam(first.HomeTeamId).Name);
    }
}
