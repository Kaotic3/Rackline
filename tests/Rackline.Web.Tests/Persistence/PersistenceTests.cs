using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Rackline.Domain.Challenges;
using Rackline.Domain.Common;
using Rackline.Domain.Leagues;
using Rackline.Domain.Matches;
using Rackline.Domain.Organisations;
using Rackline.Domain.Players;
using Rackline.Domain.Rankings;
using Rackline.Domain.Tournaments;
using Rackline.Infrastructure.Persistence;
using Rackline.Infrastructure.Persistence.Seeding;

namespace Rackline.Web.Tests.Persistence;

[Trait("Category", "Integration")]
public class PersistenceTests(PostgresDatabase database) : IClassFixture<PostgresDatabase>
{
    private static readonly DateTimeOffset At = new(2026, 5, 1, 18, 0, 0, TimeSpan.Zero);

    private static Task<Tournament> LoadTournament(RacklineDbContext db, Guid id) =>
        db.Tournaments
            .Include(t => t.Entries)
            .Include(t => t.Matches)
            .AsSplitQuery()
            .SingleAsync(t => t.Id == id);

    private static string Fingerprint(Match m) =>
        $"{m.Number}|{m.Side}|{m.Round}|{m.Position}|{m.Group}|{m.Slot1}|{m.Slot2}|{m.Score1}-{m.Score2}|{m.State}|" +
        $"{m.WinnerEntryId}|{m.LoserEntryId}|{m.WinnerTo}|{m.LoserTo}|{m.ResetMatchNumber}|{m.TableId}|{m.StartedAt}|{m.CompletedAt}|" +
        string.Join(",", m.Frames.Select(f => $"{f.Number}:{f.WinnerSlot}:{f.BreakAndRun}:{f.Points1}:{f.Points2}"));

    private async Task<(Organisation Organisation, Venue Venue, List<Player> Players)> AddBasicsAsync(int playerCount)
    {
        var organisation = new Organisation("Testshire Cue Club", OrganisationLevel.Club);
        var venue = new Venue(organisation.Id, "Hall of Nets");
        venue.AddTable("Table A");
        var players = Enumerable.Range(1, playerCount).Select(i => new Player("Test", $"Player{i}", "GB")).ToList();

        await using var db = database.CreateContext();
        db.Organisations.Add(organisation);
        db.Venues.Add(venue);
        db.Players.AddRange(players);
        await db.SaveChangesAsync();
        return (organisation, venue, players);
    }

    [PostgresFact]
    public async Task A_tournament_round_trips_and_carries_on_after_reloading()
    {
        var (organisation, venue, players) = await AddBasicsAsync(6);
        var tournament = new Tournament(organisation.Id, "Round Trip Open", Discipline.TenBall,
            TournamentFormat.DoubleElimination, MatchLength.BestOf(9), new FormatOptions(GrandFinalReset: true));
        tournament.OpenRegistration();
        players.ForEach(p => tournament.AddEntry(p.Id, seed: p == players[0] ? 1 : null, handicap: 1));
        tournament.MakeDraw(new SeededRandom(11));
        tournament.Start();

        var opener = tournament.Matches.Where(m => m.State == MatchState.Ready).MinBy(m => m.Number)!;
        tournament.StartMatch(opener.Number, At, venue.Tables[0].Id);
        tournament.RecordFrame(opener.Number, 1, At, breakAndRun: true);
        tournament.RecordFrame(opener.Number, 2, At, points1: 12, points2: 40);
        for (var i = 0; i < 4; i++)
        {
            tournament.RecordFrame(opener.Number, 1, At.AddHours(1));
        }

        await using (var db = database.CreateContext())
        {
            db.Tournaments.Add(tournament);
            await db.SaveChangesAsync();
        }

        var expected = tournament.Matches.OrderBy(m => m.Number).Select(Fingerprint).ToList();

        await using (var db = database.CreateContext())
        {
            var loaded = await LoadTournament(db, tournament.Id);

            loaded.Should().BeEquivalentTo(
                new { tournament.Name, tournament.Status, tournament.Discipline, tournament.Format, tournament.MatchLength, tournament.Options },
                o => o.ExcludingMissingMembers());
            loaded.Entries.Select(e => (e.PlayerId, e.Seed, e.Handicap))
                .Should().BeEquivalentTo(tournament.Entries.Select(e => (e.PlayerId, e.Seed, e.Handicap)));
            loaded.Matches.OrderBy(m => m.Number).Select(Fingerprint).Should().Equal(expected);

            // Play the rest of the event on the reloaded aggregate and save as we go.
            while (loaded.Matches.Where(m => m.State == MatchState.Ready).MinBy(m => m.Number) is { } next)
            {
                loaded.RecordResult(next.Number, 0, 5, At.AddHours(2));
                await db.SaveChangesAsync();
            }

            loaded.Complete();
            await db.SaveChangesAsync();
        }

        await using (var db = database.CreateContext())
        {
            var finished = await LoadTournament(db, tournament.Id);

            finished.Status.Should().Be(TournamentStatus.Completed);
            finished.Matches.Should().OnlyContain(m => m.IsResolved);
            finished.Bracket.ChampionEntryId.Should().NotBeNull();
            finished.Matches.Single(m => m.Side == BracketSide.GrandFinal && m.Round == 2).State
                .Should().Be(MatchState.Completed, "slot 2 kept winning, so the reset was played");
        }
    }

    [PostgresFact]
    public async Task Simultaneous_results_feeding_the_same_match_cannot_both_be_saved()
    {
        var (organisation, _, players) = await AddBasicsAsync(4);
        var tournament = new Tournament(organisation.Id, "Race Condition Cup", Discipline.NineBall,
            TournamentFormat.SingleElimination, MatchLength.RaceTo(3));
        tournament.OpenRegistration();
        players.ForEach(p => tournament.AddEntry(p.Id));
        tournament.MakeDraw(new SeededRandom(9));
        tournament.Start();

        await using (var db = database.CreateContext())
        {
            db.Tournaments.Add(tournament);
            await db.SaveChangesAsync();
        }

        // Two referees load the same tournament and finish both semi-finals at once.
        await using var first = database.CreateContext();
        await using var second = database.CreateContext();
        var firstCopy = await LoadTournament(first, tournament.Id);
        var secondCopy = await LoadTournament(second, tournament.Id);

        firstCopy.RecordResult(1, 3, 0, At);
        secondCopy.RecordResult(2, 3, 1, At);
        await first.SaveChangesAsync();

        await second.Invoking(db => db.SaveChangesAsync(default)).Should().ThrowAsync<DbUpdateConcurrencyException>();

        await using var check = database.CreateContext();
        var final = (await LoadTournament(check, tournament.Id)).Matches.Single(m => m.Round == 2);
        final.Slot1.HasEntry.Should().BeTrue();
        final.Slot2.IsAwaiting.Should().BeTrue("the second result was rejected, so it can be retried on fresh data");
    }

    [PostgresFact]
    public async Task Byes_and_unused_links_survive_a_round_trip()
    {
        var (organisation, _, players) = await AddBasicsAsync(5);
        var tournament = new Tournament(organisation.Id, "Bye Cup", Discipline.UsEightBall,
            TournamentFormat.SingleElimination, MatchLength.RaceTo(4));
        tournament.OpenRegistration();
        players.ForEach(p => tournament.AddEntry(p.Id));
        tournament.MakeDraw(new SeededRandom(3));

        await using (var db = database.CreateContext())
        {
            db.Tournaments.Add(tournament);
            await db.SaveChangesAsync();
        }

        await using (var db = database.CreateContext())
        {
            var loaded = await LoadTournament(db, tournament.Id);

            loaded.Matches.Count(m => m.IsBye).Should().Be(3);
            loaded.Matches.Single(m => m.WinnerTo is null).LoserTo.Should().BeNull();
            loaded.Matches.OrderBy(m => m.Number).Select(Fingerprint)
                .Should().Equal(tournament.Matches.OrderBy(m => m.Number).Select(Fingerprint));
        }
    }

    [PostgresFact]
    public async Task Other_aggregates_round_trip()
    {
        var (organisation, venue, players) = await AddBasicsAsync(4);
        players[0].JoinOrganisation(organisation.Id);

        var league = new League(organisation.Id, "Round Trip League", Discipline.EnglishEightBall);
        var season = league.AddSeason("Spring", new DateOnly(2027, 1, 5), new DateOnly(2027, 4, 30),
            new SeasonRules(1, MatchLength.BestOf(3), pointsForWin: 3, pointsForDraw: 1, pointsPerRubber: 1));
        var home = season.AddTeam("Home Side", venue.Id);
        var away = season.AddTeam("Away Side");
        season.AddPlayerToTeam(home.Id, players[0].Id);
        season.AddPlayerToTeam(away.Id, players[1].Id);
        season.GenerateFixtures(new DateTimeOffset(2027, 1, 12, 19, 0, 0, TimeSpan.Zero), daysBetweenRounds: 14, homeAndAway: true);
        var firstFixture = season.Fixtures.Single(f => f.Round == 1);
        var (homePlayer, awayPlayer) = firstFixture.HomeTeamId == home.Id ? (players[0].Id, players[1].Id) : (players[1].Id, players[0].Id);
        season.RecordResult(firstFixture.Id, [new RubberResult(homePlayer, awayPlayer, 2, 1)], At.AddMonths(9));
        season.Reschedule(season.Fixtures.Single(f => f.Round == 2).Id, null);

        var ranking = new Ranking(organisation.Id, "Round Trip Rankings", Discipline.EnglishEightBall, "2027", [40, 25], participationPoints: 5);
        var rankedTournament = new Tournament(organisation.Id, "Ranked Open", Discipline.EnglishEightBall, TournamentFormat.SingleElimination, MatchLength.RaceTo(3));
        rankedTournament.CountTowards(ranking, new OrganisationLineage(organisation.Id, new Dictionary<Guid, Guid?> { [organisation.Id] = null }));
        ranking.AwardTournament(rankedTournament.Id, [(players[0].Id, 2), (players[1].Id, 1)]);

        var rating = new Rating(players[2].Id, Discipline.EnglishEightBall);
        var opponentRating = new Rating(players[3].Id, Discipline.EnglishEightBall);
        var (ratingWon, ratingLost) = Elo.Rate(rating, opponentRating, Guid.CreateVersion7(), At);

        var challenge = new Challenge(organisation.Id, players[2].Id, players[3].Id, Discipline.Heyball, MatchLength.RaceTo(3), 1, 3, At,
            new ChallengeAssessment(false, ["Both players need a settled rating."]));

        await using (var db = database.CreateContext())
        {
            db.Players.Update(players[0]);
            db.AddRange(league, ranking, rankedTournament, rating, opponentRating, ratingWon, ratingLost, challenge);
            await db.SaveChangesAsync();
        }

        await using (var db = database.CreateContext())
        {
            (await db.Players.SingleAsync(p => p.Id == players[0].Id)).OrganisationIds.Should().Equal(organisation.Id);
            (await db.Players.CountAsync(p => p.OrganisationIds.Contains(organisation.Id))).Should().Be(1);
            (await db.Venues.Include(v => v.Tables).SingleAsync(v => v.Id == venue.Id)).Tables.Should().ContainSingle(t => t.Label == "Table A");

            var loadedLeague = await db.Leagues
                .Include(l => l.Seasons).ThenInclude(s => s.Teams)
                .Include(l => l.Seasons).ThenInclude(s => s.Fixtures)
                .AsSplitQuery()
                .SingleAsync(l => l.Id == league.Id);
            var loadedSeason = loadedLeague.Seasons.Single();
            loadedSeason.Teams.Single(t => t.Name == "Home Side").PlayerIds.Should().Equal(players[0].Id);
            loadedSeason.Should().BeEquivalentTo(new { Status = SeasonStatus.Scheduled, Rules = season.Rules }, o => o.ExcludingMissingMembers());
            var loadedFirst = loadedSeason.Fixtures.Single(f => f.Round == 1);
            loadedFirst.Should().BeEquivalentTo(new { Status = FixtureStatus.Completed, CompletedAt = At.AddMonths(9) }, o => o.ExcludingMissingMembers());
            loadedFirst.Rubbers.Single().Should().BeEquivalentTo(new { Number = 1, HomeScore = 2, AwayScore = 1 });
            loadedSeason.Fixtures.Single(f => f.Round == 2).Should().BeEquivalentTo(
                new { Status = FixtureStatus.Postponed, ScheduledFor = (DateTimeOffset?)null }, o => o.ExcludingMissingMembers());
            loadedSeason.Table().Single(r => r.TeamId == firstFixture.HomeTeamId).Points.Should().Be(3 + 1);

            var loadedRanking = await db.Rankings.SingleAsync(r => r.Id == ranking.Id);
            loadedRanking.PointsByPosition.Should().Equal(40, 25);
            loadedRanking.ParticipationPoints.Should().Be(5);
            loadedRanking.Table().Select(r => (r.PlayerId, r.Points)).Should().Equal((players[1].Id, 40), (players[0].Id, 25));
            (await db.Tournaments.SingleAsync(t => t.Id == rankedTournament.Id)).RankingId.Should().Be(ranking.Id);

            (await db.Ratings.SingleAsync(r => r.Id == rating.Id)).Should().BeEquivalentTo(new { Value = 1516.0, MatchesPlayed = 1 });
            (await db.RatingChanges.SingleAsync(c => c.Id == ratingLost.Id)).Should().BeEquivalentTo(
                new { PlayerId = players[3].Id, OpponentId = players[2].Id, Won = false, Before = 1500.0, After = 1484.0, At });

            var loadedChallenge = await db.Challenges.SingleAsync(c => c.Id == challenge.Id);
            loadedChallenge.Should().BeEquivalentTo(new
            {
                OrganisationId = organisation.Id, WinnerId = players[3].Id, PlayedAt = At, Rated = false, UnratedReason = "Both players need a settled rating.",
            });
            loadedChallenge.MatchLength.Should().Be(MatchLength.RaceTo(3));
        }
    }

    [PostgresFact]
    public async Task An_organisation_with_tournaments_cannot_be_deleted()
    {
        var (organisation, _, _) = await AddBasicsAsync(0);
        await using (var db = database.CreateContext())
        {
            db.Tournaments.Add(new Tournament(organisation.Id, "Blocker", Discipline.NineBall, TournamentFormat.RoundRobin, MatchLength.RaceTo(5)));
            await db.SaveChangesAsync();
        }

        await using (var db = database.CreateContext())
        {
            db.Organisations.Remove(await db.Organisations.SingleAsync(o => o.Id == organisation.Id));

            await db.Invoking(d => d.SaveChangesAsync(default)).Should().ThrowAsync<DbUpdateException>();
        }
    }

    [PostgresFact]
    public async Task Deleting_a_tournament_removes_its_entries_matches_and_frames()
    {
        var (organisation, _, players) = await AddBasicsAsync(2);
        var tournament = new Tournament(organisation.Id, "Short Lived", Discipline.NineBall, TournamentFormat.SingleElimination, MatchLength.RaceTo(2));
        tournament.OpenRegistration();
        players.ForEach(p => tournament.AddEntry(p.Id));
        tournament.MakeDraw(new SeededRandom(1));
        tournament.Start();
        tournament.StartMatch(1, At);
        tournament.RecordFrame(1, 1, At);

        await using (var db = database.CreateContext())
        {
            db.Tournaments.Add(tournament);
            await db.SaveChangesAsync();
        }

        await using (var db = database.CreateContext())
        {
            db.Tournaments.Remove(await LoadTournament(db, tournament.Id));
            await db.SaveChangesAsync();

            (await db.Matches.CountAsync(m => m.TournamentId == tournament.Id)).Should().Be(0);
            (await db.Database.SqlQuery<int>($"SELECT COUNT(*)::int AS \"Value\" FROM \"Frames\" WHERE \"MatchId\" = {tournament.Matches[0].Id}").SingleAsync())
                .Should().Be(0);
        }
    }
}

[Trait("Category", "Integration")]
public class SeederTests(PostgresDatabase database) : IClassFixture<PostgresDatabase>
{
    [PostgresFact]
    public async Task Seeds_invented_sample_data_once()
    {
        await using (var db = database.CreateContext())
        {
            await DevelopmentSeeder.SeedAsync(db);
        }

        await using (var db = database.CreateContext())
        {
            await DevelopmentSeeder.SeedAsync(db);
        }

        await using (var db = database.CreateContext())
        {
            (await db.Organisations.CountAsync()).Should().Be(4);
            (await db.Players.CountAsync()).Should().Be(16);
            (await db.Tournaments.CountAsync()).Should().Be(4);
            (await db.Tournaments.CountAsync(t => t.Status == TournamentStatus.Completed)).Should().Be(1);
            (await db.Matches.CountAsync(m => m.State == MatchState.Live)).Should().Be(1);
            (await db.Leagues.CountAsync()).Should().Be(1);
            var season = (await db.Leagues.Include(l => l.Seasons).ThenInclude(s => s.Fixtures).SingleAsync()).Seasons.Single();
            season.Fixtures.Count(f => f.Status == Rackline.Domain.Leagues.FixtureStatus.Completed).Should().Be(6);
            season.Fixtures.Count(f => f.Status == Rackline.Domain.Leagues.FixtureStatus.Postponed).Should().Be(1);
            var ranking = await db.Rankings.SingleAsync();
            ranking.Awards.Should().HaveCount(8);
            ranking.Table()[0].Points.Should().Be(100);
            (await db.Ratings.CountAsync()).Should().BeGreaterThan(0);
            (await db.Ratings.CountAsync()).Should().Be(await db.RatingChanges.Select(c => new { c.PlayerId, c.Discipline }).Distinct().CountAsync());
            (await db.Ratings.SumAsync(r => r.MatchesPlayed)).Should().Be(await db.RatingChanges.CountAsync());
            (await db.Challenges.CountAsync()).Should().Be(2);
            (await db.Challenges.CountAsync(c => c.Rated)).Should().Be(0, "the sample players' ratings are all provisional");

            var springOpen = await db.Tournaments.Include(t => t.Matches).SingleAsync(t => t.Name == "Eastbridge Spring Open");
            springOpen.Status.Should().Be(TournamentStatus.InProgress);
            springOpen.Matches.Count(m => m.State == MatchState.Completed).Should().Be(2);
        }
    }
}
