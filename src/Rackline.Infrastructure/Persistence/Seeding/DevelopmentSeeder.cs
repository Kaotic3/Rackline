using Microsoft.EntityFrameworkCore;
using Rackline.Domain.Challenges;
using Rackline.Domain.Common;
using Rackline.Domain.Leagues;
using Rackline.Domain.Matches;
using Rackline.Domain.Organisations;
using Rackline.Domain.Players;
using Rackline.Domain.Rankings;
using Rackline.Domain.Standings;
using Rackline.Domain.Tournaments;

namespace Rackline.Infrastructure.Persistence.Seeding;

/// <summary>
/// Sample data for local development. Every name, place and result here is invented.
/// </summary>
public static class DevelopmentSeeder
{
    private static readonly DateTimeOffset SpringOpenStart = new(2026, 3, 14, 10, 0, 0, TimeSpan.Zero);

    private static readonly (string First, string Last, string Nationality)[] PlayerNames =
    [
        ("Ada", "Marlow", "GB"), ("Bram", "Keswick", "GB"), ("Cara", "Dunmore", "IE"), ("Dev", "Ashcombe", "GB"),
        ("Elin", "Vasquell", "NL"), ("Finn", "Holloway", "IE"), ("Greta", "Lindqvar", "SE"), ("Hal", "Penrose", "GB"),
        ("Isla", "Brennick", "GB"), ("Jonas", "Weltzer", "DE"), ("Kit", "Ormsby", "GB"), ("Lena", "Castrell", "ES"),
        ("Milo", "Thursby", "GB"), ("Nia", "Aberfoyle", "GB"), ("Oskar", "Brandt-Velling", "DK"), ("Pia", "Morcant", "FR"),
    ];

    public static async Task SeedAsync(RacklineDbContext db, CancellationToken cancellationToken = default)
    {
        if (await db.Organisations.AnyAsync(cancellationToken))
        {
            return;
        }

        var union = new Organisation("Global Cue Alliance", OrganisationLevel.International);
        var federation = new Organisation("Northland Pool Federation", OrganisationLevel.National, union);
        var region = new Organisation("Northland East", OrganisationLevel.Regional, federation);
        var club = new Organisation("Corner Pocket Club", OrganisationLevel.Club, region);
        db.Organisations.AddRange(union, federation, region, club);

        var venue = new Venue(club.Id, "The Green Baize Rooms", "1 Example Street, Eastbridge");
        var tables = Enumerable.Range(1, 6).Select(i => venue.AddTable($"Table {i}")).ToList();
        db.Venues.Add(venue);

        var players = PlayerNames
            .Select((p, i) => new Player(p.First, p.Last, p.Nationality, $"NPF-{i + 1:0000}"))
            .ToList();
        foreach (var player in players)
        {
            player.JoinOrganisation(federation.Id);
            player.JoinOrganisation(club.Id);
        }

        db.Players.AddRange(players);

        var ranking = NineBallRanking(federation);
        db.Rankings.Add(ranking);

        Tournament[] tournaments =
        [
            SpringOpen(federation, players, tables, ranking),
            ClubNight(club, players),
            SnookerClassic(club, players),
            WinterClassic(federation, players, tables, ranking),
        ];
        db.Tournaments.AddRange(tournaments);

        // Ratings come from the results above, exactly as if each had been rated when it was played.
        var (ratings, changes) = Elo.Replay(tournaments.SelectMany(RatedResult.From));
        db.Ratings.AddRange(ratings);
        db.RatingChanges.AddRange(changes);

        db.Leagues.Add(TuesdayLeague(club, venue, players));
        db.Challenges.Add(AcceptedChallenge(players));

        await db.SaveChangesAsync(cancellationToken);
    }

    /// <summary>A 16-player double-elimination ranking event part-way through its first round.</summary>
    private static Tournament SpringOpen(Organisation owner, List<Player> players, List<Table> tables, Ranking ranking)
    {
        var tournament = new Tournament(owner.Id, "Eastbridge Spring Open", Discipline.NineBall,
            TournamentFormat.DoubleElimination, MatchLength.RaceTo(7));
        tournament.CountTowards(ranking);
        tournament.OpenRegistration();
        for (var i = 0; i < players.Count; i++)
        {
            tournament.AddEntry(players[i].Id, seed: i < 4 ? i + 1 : null);
        }

        tournament.MakeDraw(new SeededRandom(2026));
        tournament.Start();

        var firstRound = tournament.Matches
            .Where(m => m.Side == BracketSide.Winners && m.Round == 1)
            .OrderBy(m => m.Position)
            .ToList();

        tournament.StartMatch(firstRound[0].Number, SpringOpenStart, tables[0].Id);
        tournament.RecordResult(firstRound[0].Number, 7, 3, SpringOpenStart.AddHours(1));
        tournament.StartMatch(firstRound[1].Number, SpringOpenStart, tables[1].Id);
        tournament.RecordResult(firstRound[1].Number, 5, 7, SpringOpenStart.AddMinutes(75));

        var live = firstRound[2];
        tournament.StartMatch(live.Number, SpringOpenStart.AddMinutes(10), tables[2].Id);
        int[] frameWinners = [1, 2, 2, 1, 2, 1, 2, 1, 2];
        for (var i = 0; i < frameWinners.Length; i++)
        {
            tournament.RecordFrame(live.Number, frameWinners[i], SpringOpenStart.AddMinutes(15 + i * 8), breakAndRun: i == 0);
        }

        return tournament;
    }

    /// <summary>An 8-player English 8-ball round robin in two groups.</summary>
    private static Tournament ClubNight(Organisation owner, List<Player> players)
    {
        var tournament = new Tournament(owner.Id, "Corner Pocket Club Night", Discipline.EnglishEightBall,
            TournamentFormat.RoundRobin, MatchLength.RaceTo(3), new FormatOptions(GroupCount: 2));
        tournament.OpenRegistration();
        foreach (var player in players.Skip(4).Take(8))
        {
            tournament.AddEntry(player.Id);
        }

        tournament.MakeDraw(new SeededRandom(7));
        tournament.Start();

        var at = new DateTimeOffset(2026, 4, 2, 19, 30, 0, TimeSpan.Zero);
        foreach (var match in tournament.Matches.Where(m => m.Round == 1))
        {
            tournament.RecordResult(match.Number, 3, match.Number % 3, at);
        }

        return tournament;
    }

    /// <summary>
    /// A finished 8-player 9-ball knockout, scored frame by frame, so profiles and head-to-heads have history.
    /// The better seed wins every match except one upset in the first round. It counts towards the ranking.
    /// </summary>
    private static Tournament WinterClassic(Organisation owner, List<Player> players, List<Table> tables, Ranking ranking)
    {
        var tournament = new Tournament(owner.Id, "Winter Nine-Ball Classic", Discipline.NineBall,
            TournamentFormat.SingleElimination, MatchLength.RaceTo(5));
        tournament.CountTowards(ranking);
        tournament.OpenRegistration();
        var seeds = players.Take(8).Select((p, i) => tournament.AddEntry(p.Id, seed: i + 1)).ToList();
        var seedOf = seeds.ToDictionary(e => e.Id, e => e.Seed!.Value);

        tournament.MakeDraw(new SeededRandom(1));
        tournament.Start();

        var at = new DateTimeOffset(2026, 1, 17, 10, 0, 0, TimeSpan.Zero);
        var frame = 0;
        while (tournament.Matches.Where(m => m.State == MatchState.Ready).MinBy(m => m.Number) is { } match)
        {
            tournament.StartMatch(match.Number, at, tables[match.Number % tables.Count].Id);

            var better = seedOf[match.Slot1.EntryId!.Value] < seedOf[match.Slot2.EntryId!.Value] ? 1 : 2;
            var winner = match.Number == 4 ? 3 - better : better; // seed 6 beats seed 3
            var loserFrames = match.Number % 5;
            for (var i = 0; i < loserFrames; i++)
            {
                tournament.RecordFrame(match.Number, 3 - winner, at, breakAndRun: ++frame % 7 == 0);
            }

            for (var i = 0; i < 5; i++)
            {
                tournament.RecordFrame(match.Number, winner, at.AddMinutes(10 * (i + loserFrames)), breakAndRun: ++frame % 4 == 0);
            }

            at = at.AddHours(1);
        }

        tournament.Complete();
        var playerOf = tournament.Entries.ToDictionary(e => e.Id, e => e.PlayerId);
        ranking.AwardTournament(tournament.Id, FinishingPositions.Calculate(tournament).Select(p => (playerOf[p.EntryId], p.Position)));
        return tournament;
    }

    /// <summary>A snooker event still taking entries.</summary>
    private static Tournament SnookerClassic(Organisation owner, List<Player> players)
    {
        var tournament = new Tournament(owner.Id, "Summer Snooker Classic", Discipline.Snooker,
            TournamentFormat.SingleElimination, MatchLength.BestOf(5));
        tournament.OpenRegistration();
        foreach (var player in players.Take(5))
        {
            tournament.AddEntry(player.Id, handicap: 0);
        }

        return tournament;
    }

    /// <summary>
    /// A four-team English 8-ball league, home and away from 8 September 2026: four singles rubbers,
    /// each a race to 2, with 3 points for a win and 1 for a draw. Rounds 1–3 are played and one
    /// round-4 fixture is postponed.
    /// </summary>
    private static League TuesdayLeague(Organisation owner, Venue venue, List<Player> players)
    {
        var league = new League(owner.Id, "Tuesday Night League", Discipline.EnglishEightBall);
        var season = league.AddSeason("Autumn 2026", new DateOnly(2026, 9, 1), new DateOnly(2026, 12, 15),
            new SeasonRules(4, MatchLength.RaceTo(2), pointsForWin: 3, pointsForDraw: 1));

        string[] teamNames = ["Cushion Kings", "Pocket Rockets", "The Long Pots", "Baulk Line Rovers"];
        var teams = teamNames.Select(name => season.AddTeam(name, venue.Id)).ToList();
        for (var i = 0; i < players.Count; i++)
        {
            season.AddPlayerToTeam(teams[i % teams.Count].Id, players[i].Id);
        }

        var firstNight = new DateTimeOffset(2026, 9, 8, 19, 30, 0, TimeSpan.Zero);
        season.GenerateFixtures(firstNight);

        // Invented results: the rubber pattern varies by fixture so the table has a spread.
        int[][][] patterns =
        [
            [[2, 1], [0, 2], [2, 0], [2, 1]],
            [[1, 2], [2, 0], [0, 2], [2, 1]],
            [[2, 0], [2, 1], [2, 1], [0, 2]],
            [[0, 2], [1, 2], [2, 0], [1, 2]],
        ];
        var played = season.Fixtures.Where(f => f.Round <= 3).OrderBy(f => f.Round).ToList();
        for (var f = 0; f < played.Count; f++)
        {
            var fixture = played[f];
            var home = season.FindTeam(fixture.HomeTeamId).PlayerIds.ToList();
            var away = season.FindTeam(fixture.AwayTeamId).PlayerIds.ToList();
            var pattern = patterns[f % patterns.Length];
            var rubbers = pattern.Select((score, i) => new RubberResult(home[i], away[(i + f) % away.Count], score[0], score[1])).ToList();
            season.RecordResult(fixture.Id, rubbers, fixture.ScheduledFor!.Value.AddHours(3));
        }

        season.Reschedule(season.Fixtures.First(f => f.Round == 4).Id, null);
        return league;
    }

    private static Ranking NineBallRanking(Organisation owner) =>
        new(owner.Id, "Northland 9-ball rankings", Discipline.NineBall, "2026", [100, 75, 55, 40, 25, 15], participationPoints: 5);

    private static Challenge AcceptedChallenge(List<Player> players)
    {
        var challenge = new Challenge(players[14].Id, players[15].Id, Discipline.Heyball, MatchLength.RaceTo(5));
        challenge.Accept();
        return challenge;
    }
}
