using Microsoft.EntityFrameworkCore;
using Rackline.Domain.Challenges;
using Rackline.Domain.Common;
using Rackline.Domain.Leagues;
using Rackline.Domain.Matches;
using Rackline.Domain.Organisations;
using Rackline.Domain.Players;
using Rackline.Domain.Rankings;
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

        db.Tournaments.AddRange(
            SpringOpen(federation, players, tables),
            ClubNight(club, players),
            SnookerClassic(club, players));

        db.Leagues.Add(TuesdayLeague(club, venue, players));
        db.Rankings.Add(NineBallRanking(federation, players));
        db.Ratings.AddRange(players.Select((p, i) => Rated(p, 1650 - i * 15)));
        db.Challenges.Add(AcceptedChallenge(players));

        await db.SaveChangesAsync(cancellationToken);
    }

    /// <summary>A 16-player double-elimination event part-way through its first round.</summary>
    private static Tournament SpringOpen(Organisation owner, List<Player> players, List<Table> tables)
    {
        var tournament = new Tournament(owner.Id, "Eastbridge Spring Open", Discipline.NineBall,
            TournamentFormat.DoubleElimination, MatchLength.RaceTo(7));
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
        live.UpdateScore(4, 5);
        live.RecordFrame(1, breakAndRun: true);

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

    private static League TuesdayLeague(Organisation owner, Venue venue, List<Player> players)
    {
        var league = new League(owner.Id, "Tuesday Night League", Discipline.EnglishEightBall);
        var season = league.AddSeason("Autumn 2026", new DateOnly(2026, 9, 1), new DateOnly(2026, 12, 15));

        string[] teamNames = ["Cushion Kings", "Pocket Rockets", "The Long Pots", "Baulk Line Rovers"];
        var teams = teamNames.Select(name => season.AddTeam(name, venue.Id)).ToList();
        for (var i = 0; i < players.Count; i++)
        {
            teams[i % teams.Count].AddPlayer(players[i].Id);
        }

        var fixture = season.AddFixture(teams[0].Id, teams[1].Id, new DateTimeOffset(2026, 9, 8, 19, 30, 0, TimeSpan.Zero));
        var home = teams[0].PlayerIds.ToList();
        var away = teams[1].PlayerIds.ToList();
        int[][] scores = [[2, 1], [0, 2], [2, 0], [1, 2]];
        for (var i = 0; i < scores.Length; i++)
        {
            fixture.AddRubber(home[i], away[i], scores[i][0], scores[i][1]);
        }

        season.AddFixture(teams[2].Id, teams[3].Id, new DateTimeOffset(2026, 9, 8, 19, 30, 0, TimeSpan.Zero));
        return league;
    }

    private static Ranking NineBallRanking(Organisation owner, List<Player> players)
    {
        var ranking = new Ranking(owner.Id, Discipline.NineBall, "2026");
        for (var i = 0; i < 8; i++)
        {
            ranking.AwardPoints(players[i].Id, 100 - i * 10);
        }

        return ranking;
    }

    private static Rating Rated(Player player, double value)
    {
        var rating = new Rating(player.Id, Discipline.NineBall);
        rating.Apply(value);
        return rating;
    }

    private static Challenge AcceptedChallenge(List<Player> players)
    {
        var challenge = new Challenge(players[14].Id, players[15].Id, Discipline.Heyball, MatchLength.RaceTo(5));
        challenge.Accept();
        return challenge;
    }
}
