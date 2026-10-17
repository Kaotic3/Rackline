using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using System.Text.Json.Serialization;
using FluentAssertions;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Testing;
using Rackline.Application.Common;
using Rackline.Domain.Organisations;
using Rackline.Application.Leagues;
using Rackline.Domain.Leagues;
using Rackline.Web.Api;
using Rackline.Web.Tests.Persistence;

namespace Rackline.Web.Tests.Api;

[Trait("Category", "Integration")]
public class LeagueApiTests(ApiFactory api) : IClassFixture<ApiFactory>
{
    private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web) { Converters = { new JsonStringEnumConverter() } };
    private static readonly DateTimeOffset FirstNight = new(2026, 9, 8, 19, 30, 0, TimeSpan.Zero);

    private static async Task<T> ReadAsync<T>(HttpResponseMessage response)
    {
        response.IsSuccessStatusCode.Should().BeTrue(await response.Content.ReadAsStringAsync());
        return (await response.Content.ReadFromJsonAsync<T>(Json))!;
    }

    /// <summary>A league with one season of two teams (two players each), fixtures made home and away.</summary>
    private async Task<(HttpClient Organiser, SeasonResponse Season)> ScheduledSeasonAsync()
    {
        var (organisationId, _, players) = await api.AddReferenceDataAsync(4);
        var venueId = await VenueOfAsync(organisationId);
        var organiser = await api.ClientForAsync(StaffRole.Organiser);

        var league = await ReadAsync<LeagueSummaryDto>(await organiser.PostAsJsonAsync("/api/leagues",
            new { organisationId, name = $"Test League {Guid.NewGuid():N}"[..20], discipline = "EnglishEightBall" }));
        var season = await ReadAsync<SeasonResponse>(await organiser.PostAsJsonAsync($"/api/leagues/{league.Id}/seasons", new
        {
            name = "Autumn 2026", startsOn = "2026-09-01", endsOn = "2026-12-15",
            rubbersPerFixture = 2, rubberLengthKind = "RaceTo", rubberLengthValue = 2, pointsForWin = 3, pointsForDraw = 1,
        }));
        var id = season.Season.Id;

        await organiser.PostAsJsonAsync($"/api/seasons/{id}/teams", new { name = "Cushion Kings", homeVenueId = venueId });
        season = await ReadAsync<SeasonResponse>(await organiser.PostAsJsonAsync($"/api/seasons/{id}/teams", new { name = "Pocket Rockets" }));
        var kings = season.Season.Teams.Single(t => t.Name == "Cushion Kings");
        var rockets = season.Season.Teams.Single(t => t.Name == "Pocket Rockets");
        for (var i = 0; i < 4; i++)
        {
            (await organiser.PutAsync($"/api/seasons/{id}/teams/{(i < 2 ? kings.Id : rockets.Id)}/players/{players[i]}", null)).EnsureSuccessStatusCode();
        }

        season = await ReadAsync<SeasonResponse>(await organiser.PostAsJsonAsync($"/api/seasons/{id}/fixtures",
            new { firstRound = "2026-09-08T20:30:00+01:00", daysBetweenRounds = 7, homeAndAway = true }));
        return (organiser, season);
    }

    private async Task<Guid> VenueOfAsync(Guid organisationId)
    {
        await using var db = api.CreateDbContext();
        return db.Venues.Single(v => v.OrganisationId == organisationId).Id;
    }

    private static object ResultFor(SeasonDetailDto season, FixtureDto fixture, params (int Home, int Away)[] scores)
    {
        var home = season.Teams.Single(t => t.Id == fixture.HomeTeamId).Players;
        var away = season.Teams.Single(t => t.Id == fixture.AwayTeamId).Players;
        return new { rubbers = scores.Select((s, i) => new { homePlayerId = home[i].Id, awayPlayerId = away[i].Id, homeScore = s.Home, awayScore = s.Away }) };
    }

    [PostgresFact]
    public async Task A_season_runs_from_set_up_to_a_final_table()
    {
        var (organiser, season) = await ScheduledSeasonAsync();
        var s = season.Season;

        s.Status.Should().Be(SeasonStatus.Scheduled);
        s.Teams.Should().OnlyContain(t => t.Players.Count == 2 && t.Players.All(p => p.Name.StartsWith("Testy McTest")));
        s.Teams.Single(t => t.Name == "Cushion Kings").HomeVenueName.Should().Be("Test Hall");
        s.Fixtures.Should().HaveCount(2);
        s.Fixtures.Single(f => f.Round == 1).ScheduledFor.Should().Be(FirstNight, "times are stored in UTC");
        s.Fixtures.Single(f => f.Round == 2).ScheduledFor.Should().Be(FirstNight.AddDays(7));

        var first = s.Fixtures.Single(f => f.Round == 1);
        var afterFirst = await ReadAsync<SeasonResponse>(await organiser.PutAsJsonAsync($"/api/seasons/{s.Id}/fixtures/{first.Id}/result", ResultFor(s, first, (2, 1), (2, 0))));
        var second = s.Fixtures.Single(f => f.Round == 2);
        var afterSecond = await ReadAsync<SeasonResponse>(await organiser.PutAsJsonAsync($"/api/seasons/{s.Id}/fixtures/{second.Id}/result", ResultFor(s, second, (0, 2), (2, 1))));

        afterFirst.Table[0].Should().BeEquivalentTo(new { TeamId = first.HomeTeamId, Points = 3, Won = 1, RubbersFor = 2 }, o => o.ExcludingMissingMembers());
        afterSecond.Table.Should().OnlyContain(r => r.Played == 2);
        afterSecond.Table[0].Should().BeEquivalentTo(new { TeamId = first.HomeTeamId, Points = 4, Won = 1, Drawn = 1, RubbersFor = 3, RubbersAgainst = 1 }, o => o.ExcludingMissingMembers());
        afterSecond.Season.Fixtures.Should().OnlyContain(f => f.Status == FixtureStatus.Completed);

        var done = await ReadAsync<SeasonResponse>(await organiser.PostAsync($"/api/seasons/{s.Id}/complete", null));
        done.Season.Status.Should().Be(SeasonStatus.Completed);

        var listed = await api.CreateClient().GetFromJsonAsync<List<LeagueSummaryDto>>("/api/leagues", Json);
        listed!.Single(l => l.Id == s.LeagueId).Seasons.Single().Should().BeEquivalentTo(new { TeamCount = 2, Status = SeasonStatus.Completed }, o => o.ExcludingMissingMembers());
    }

    [PostgresFact]
    public async Task Fixtures_can_be_postponed_and_given_a_new_date()
    {
        var (organiser, season) = await ScheduledSeasonAsync();
        var fixture = season.Season.Fixtures.Single(f => f.Round == 2);

        var postponed = await ReadAsync<SeasonResponse>(await organiser.PostAsJsonAsync($"/api/seasons/{season.Season.Id}/fixtures/{fixture.Id}/reschedule", new { scheduledFor = (DateTimeOffset?)null }));
        var moved = await ReadAsync<SeasonResponse>(await organiser.PostAsJsonAsync($"/api/seasons/{season.Season.Id}/fixtures/{fixture.Id}/reschedule", new { scheduledFor = "2026-10-01T19:00:00Z" }));

        postponed.Season.Fixtures.Single(f => f.Id == fixture.Id).Should().BeEquivalentTo(new { Status = FixtureStatus.Postponed, ScheduledFor = (DateTimeOffset?)null }, o => o.ExcludingMissingMembers());
        moved.Season.Fixtures.Single(f => f.Id == fixture.Id).Should().BeEquivalentTo(
            new { Status = FixtureStatus.Scheduled, ScheduledFor = new DateTimeOffset(2026, 10, 1, 19, 0, 0, TimeSpan.Zero) }, o => o.ExcludingMissingMembers());
    }

    [PostgresFact]
    public async Task Reading_is_public_and_changes_need_an_organiser()
    {
        var (_, season) = await ScheduledSeasonAsync();
        var anonymous = api.CreateClient();
        var referee = await api.ClientForAsync(StaffRole.Referee);
        var fixture = season.Season.Fixtures[0];

        (await anonymous.GetAsync($"/api/seasons/{season.Season.Id}")).StatusCode.Should().Be(HttpStatusCode.OK);
        (await anonymous.GetAsync($"/api/leagues/{season.Season.LeagueId}")).StatusCode.Should().Be(HttpStatusCode.OK);
        (await anonymous.PostAsJsonAsync("/api/leagues", new { })).StatusCode.Should().Be(HttpStatusCode.Unauthorized);
        (await referee.PutAsJsonAsync($"/api/seasons/{season.Season.Id}/fixtures/{fixture.Id}/result", ResultFor(season.Season, fixture, (2, 0), (2, 0))))
            .StatusCode.Should().Be(HttpStatusCode.Forbidden);
    }

    [PostgresFact]
    public async Task Bad_requests_missing_things_and_rule_breaches_are_reported()
    {
        var (organiser, season) = await ScheduledSeasonAsync();
        var s = season.Season;
        var fixture = s.Fixtures[0];

        var evenBestOf = await organiser.PostAsJsonAsync($"/api/leagues/{s.LeagueId}/seasons", new
        {
            name = "Spring", startsOn = "2027-01-01", endsOn = "2027-03-01", rubbersPerFixture = 4, rubberLengthKind = "BestOf", rubberLengthValue = 4,
        });
        var backwards = await organiser.PostAsJsonAsync($"/api/leagues/{s.LeagueId}/seasons", new
        {
            name = "Spring", startsOn = "2027-03-01", endsOn = "2027-01-01", rubbersPerFixture = 4, rubberLengthKind = "RaceTo", rubberLengthValue = 1,
        });
        var noRubbers = await organiser.PutAsJsonAsync($"/api/seasons/{s.Id}/fixtures/{fixture.Id}/result", new { rubbers = Array.Empty<object>() });
        var unknownSeason = await organiser.GetAsync($"/api/seasons/{Guid.NewGuid()}");
        var unknownFixture = await organiser.PutAsJsonAsync($"/api/seasons/{s.Id}/fixtures/{Guid.NewGuid()}/result", ResultFor(s, fixture, (2, 0), (2, 0)));
        var unknownPlayer = await organiser.PutAsync($"/api/seasons/{s.Id}/teams/{s.Teams[0].Id}/players/{Guid.NewGuid()}", null);
        var drawnRubber = await organiser.PutAsJsonAsync($"/api/seasons/{s.Id}/fixtures/{fixture.Id}/result", ResultFor(s, fixture, (2, 2), (2, 0)));
        var lateTeam = await organiser.PostAsJsonAsync($"/api/seasons/{s.Id}/teams", new { name = "Latecomers" });
        var tooEarlyToFinish = await organiser.PostAsync($"/api/seasons/{s.Id}/complete", null);

        evenBestOf.StatusCode.Should().Be(HttpStatusCode.BadRequest);
        backwards.StatusCode.Should().Be(HttpStatusCode.BadRequest);
        noRubbers.StatusCode.Should().Be(HttpStatusCode.BadRequest);
        unknownSeason.StatusCode.Should().Be(HttpStatusCode.NotFound);
        unknownFixture.StatusCode.Should().Be(HttpStatusCode.NotFound);
        unknownPlayer.StatusCode.Should().Be(HttpStatusCode.NotFound);
        drawnRubber.StatusCode.Should().Be(HttpStatusCode.Conflict);
        (await drawnRubber.Content.ReadFromJsonAsync<ProblemDetails>())!.Detail.Should().Contain("exactly one player must reach 2");
        lateTeam.StatusCode.Should().Be(HttpStatusCode.Conflict, "teams are fixed once the fixtures exist");
        tooEarlyToFinish.StatusCode.Should().Be(HttpStatusCode.Conflict);
    }

    [PostgresFact]
    public async Task A_player_cannot_join_two_teams_in_one_season()
    {
        var (organiser, season) = await ScheduledSeasonAsync();
        var s = season.Season;
        var kingsPlayer = s.Teams.Single(t => t.Name == "Cushion Kings").Players[0].Id;
        var rockets = s.Teams.Single(t => t.Name == "Pocket Rockets").Id;

        var response = await organiser.PutAsync($"/api/seasons/{s.Id}/teams/{rockets}/players/{kingsPlayer}", null);

        response.StatusCode.Should().Be(HttpStatusCode.Conflict);
    }

    [PostgresFact]
    public async Task Public_league_pages_show_the_table_fixtures_and_rubbers()
    {
        var (organiser, season) = await ScheduledSeasonAsync();
        var s = season.Season;
        var first = s.Fixtures.Single(f => f.Round == 1);
        await organiser.PutAsJsonAsync($"/api/seasons/{s.Id}/fixtures/{first.Id}/result", ResultFor(s, first, (2, 1), (1, 2)));
        await organiser.PostAsJsonAsync($"/api/seasons/{s.Id}/fixtures/{s.Fixtures.Single(f => f.Round == 2).Id}/reschedule", new { scheduledFor = (DateTimeOffset?)null });
        var visitor = api.CreateClient();

        var leagues = WebUtility.HtmlDecode(await visitor.GetStringAsync("/leagues"));
        var seasonPage = WebUtility.HtmlDecode(await visitor.GetStringAsync($"/seasons/{s.Id}"));
        var fixturePage = WebUtility.HtmlDecode(await visitor.GetStringAsync($"/seasons/{s.Id}/fixtures/{first.Id}"));

        leagues.Should().Contain(s.LeagueName);
        seasonPage.Should().Contain("<table class=\"standings\">").And.Contain("Week 1").And.Contain("1–1").And.Contain("Postponed");
        seasonPage.Should().Contain("2 rubbers a match, each a race to 2").And.Contain("3 points for a win, 1 for a draw");
        seasonPage.Should().NotContain("Enter result", "visitors cannot enter results");
        fixturePage.Should().Contain("Testy McTest1").And.Contain("2–1").And.Contain("1–2");
        (await visitor.GetAsync($"/seasons/{s.Id}/fixtures/{Guid.NewGuid()}")).StatusCode.Should().Be(HttpStatusCode.NotFound);
        (await visitor.GetAsync($"/seasons/{Guid.NewGuid()}")).StatusCode.Should().Be(HttpStatusCode.NotFound);
        (await visitor.GetAsync($"/leagues/{Guid.NewGuid()}")).StatusCode.Should().Be(HttpStatusCode.NotFound);
    }

    [PostgresFact]
    public async Task The_result_entry_page_is_for_organisers_only()
    {
        var (_, season) = await ScheduledSeasonAsync();
        var path = $"/organiser/seasons/{season.Season.Id}/fixtures/{season.Season.Fixtures[0].Id}";
        var browser = api.CreateClient(new WebApplicationFactoryClientOptions { AllowAutoRedirect = false });

        var response = await browser.GetAsync(path);

        response.StatusCode.Should().Be(HttpStatusCode.Redirect);
        response.Headers.Location!.PathAndQuery.Should().StartWith("/account/login");
    }

    [PostgresFact]
    public async Task Result_links_appear_only_for_organisers_of_the_league_s_organisation()
    {
        var (_, season) = await ScheduledSeasonAsync();
        var s = season.Season;
        var (otherClub, _, _) = await api.AddReferenceDataAsync(0);

        var own = await api.BrowserSignedInAsync(await api.CreateUserAsync(StaffRole.Organiser, s.OrganisationId));
        var other = await api.BrowserSignedInAsync(await api.CreateUserAsync(StaffRole.Organiser, otherClub));

        WebUtility.HtmlDecode(await own.GetStringAsync($"/seasons/{s.Id}")).Should().Contain("Enter result");
        WebUtility.HtmlDecode(await own.GetStringAsync($"/seasons/{s.Id}/fixtures/{s.Fixtures[0].Id}")).Should().Contain("Enter the result");
        WebUtility.HtmlDecode(await other.GetStringAsync($"/seasons/{s.Id}")).Should().NotContain("Enter result");

        // The API agrees with the pages.
        var otherApi = await api.ClientForAsync(StaffRole.Organiser, otherClub);
        (await otherApi.PostAsync($"/api/seasons/{s.Id}/complete", null)).StatusCode.Should().Be(HttpStatusCode.Forbidden);
    }
}
