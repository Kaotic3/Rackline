using FluentAssertions;
using Rackline.Domain.Challenges;
using Rackline.Domain.Common;
using Rackline.Domain.Leagues;
using Rackline.Domain.Organisations;
using Rackline.Domain.Players;
using Rackline.Domain.Rankings;

namespace Rackline.Domain.Tests;

public class OrganisationTests
{
    [Fact]
    public void Children_must_sit_below_their_parent()
    {
        var federation = new Organisation("Northland Pool Federation", OrganisationLevel.National);
        var region = new Organisation("Northland East", OrganisationLevel.Regional, federation);

        region.ParentId.Should().Be(federation.Id);
        FluentActions.Invoking(() => new Organisation("Peer", OrganisationLevel.National, federation)).Should().Throw<DomainException>();
        FluentActions.Invoking(() => new Organisation("Upside down", OrganisationLevel.International, region)).Should().Throw<DomainException>();
    }

    [Fact]
    public void Names_are_required_and_trimmed()
    {
        var club = new Organisation("  Corner Pocket Club ", OrganisationLevel.Club);
        club.Name.Should().Be("Corner Pocket Club");

        club.Invoking(c => c.Rename("")).Should().Throw<DomainException>();
        FluentActions.Invoking(() => new Organisation(" ", OrganisationLevel.Club)).Should().Throw<DomainException>();
    }

    [Fact]
    public void Venue_table_labels_are_unique()
    {
        var venue = new Venue(Guid.CreateVersion7(), "The Green Baize Rooms", "1 Example Street");

        var table = venue.AddTable("Table 1");

        table.VenueId.Should().Be(venue.Id);
        venue.Invoking(v => v.AddTable("table 1")).Should().Throw<DomainException>();
        venue.AddTable("Table 2");
        venue.Tables.Should().HaveCount(2);
    }
}

public class PlayerTests
{
    [Fact]
    public void Nationality_is_a_two_letter_code()
    {
        new Player("Ada", "Example", "gb").Nationality.Should().Be("GB");

        FluentActions.Invoking(() => new Player("Ada", "Example", "GBR")).Should().Throw<DomainException>();
        FluentActions.Invoking(() => new Player("Ada", "Example", "G1")).Should().Throw<DomainException>();
    }

    [Fact]
    public void Tracks_organisation_memberships()
    {
        var player = new Player("Ada", "Example", "IE", "LIC-0001");
        var club = Guid.CreateVersion7();

        player.JoinOrganisation(club);
        player.JoinOrganisation(club);
        player.OrganisationIds.Should().ContainSingle();

        player.LeaveOrganisation(club);
        player.OrganisationIds.Should().BeEmpty();
        player.DisplayName.Should().Be("Ada Example");
        player.LicenceNumber.Should().Be("LIC-0001");
    }
}

public class LeagueTests
{
    private static readonly DateOnly Autumn = new(2026, 9, 1);

    [Fact]
    public void Seasons_cannot_overlap_or_run_backwards()
    {
        var league = new League(Guid.CreateVersion7(), "Tuesday Night League", Discipline.EnglishEightBall);
        league.AddSeason("Autumn 2026", Autumn, Autumn.AddMonths(3));

        league.Invoking(l => l.AddSeason("Clash", Autumn.AddMonths(2), Autumn.AddMonths(5))).Should().Throw<DomainException>();
        league.Invoking(l => l.AddSeason("Backwards", Autumn.AddMonths(6), Autumn.AddMonths(5))).Should().Throw<DomainException>();
        league.AddSeason("Spring 2027", Autumn.AddMonths(4), Autumn.AddMonths(7));
        league.Seasons.Should().HaveCount(2);
    }

    [Fact]
    public void Fixtures_are_between_two_different_teams_in_the_season()
    {
        var season = new League(Guid.CreateVersion7(), "Tuesday Night League", Discipline.EnglishEightBall)
            .AddSeason("Autumn 2026", Autumn, Autumn.AddMonths(3));
        var home = season.AddTeam("Cushion Kings");
        var away = season.AddTeam("Pocket Rockets");

        season.Invoking(s => s.AddTeam("cushion kings")).Should().Throw<DomainException>();
        season.Invoking(s => s.AddFixture(home.Id, home.Id)).Should().Throw<DomainException>();
        season.Invoking(s => s.AddFixture(home.Id, Guid.CreateVersion7())).Should().Throw<DomainException>();

        var fixture = season.AddFixture(home.Id, away.Id);
        fixture.AddRubber(Guid.CreateVersion7(), Guid.CreateVersion7(), 2, 1);
        fixture.AddRubber(Guid.CreateVersion7(), Guid.CreateVersion7(), 0, 2);
        fixture.AddRubber(Guid.CreateVersion7(), Guid.CreateVersion7(), 2, 0);

        fixture.HomeRubbersWon.Should().Be(2);
        fixture.AwayRubbersWon.Should().Be(1);
        fixture.Rubbers.Select(r => r.Number).Should().Equal(1, 2, 3);
    }

    [Fact]
    public void Team_rosters_hold_each_player_once()
    {
        var team = new League(Guid.CreateVersion7(), "League", Discipline.NineBall)
            .AddSeason("S1", Autumn, Autumn).AddTeam("Team");
        var player = Guid.CreateVersion7();

        team.AddPlayer(player);
        team.AddPlayer(player);
        team.PlayerIds.Should().ContainSingle();
        team.RemovePlayer(player);
        team.PlayerIds.Should().BeEmpty();
    }

    [Fact]
    public void Rubbers_reject_self_play_and_negative_scores()
    {
        var season = new League(Guid.CreateVersion7(), "League", Discipline.NineBall).AddSeason("S1", Autumn, Autumn);
        var fixture = season.AddFixture(season.AddTeam("A").Id, season.AddTeam("B").Id);
        var player = Guid.CreateVersion7();

        fixture.Invoking(f => f.AddRubber(player, player, 1, 0)).Should().Throw<DomainException>();
        fixture.Invoking(f => f.AddRubber(player, Guid.CreateVersion7(), -1, 0)).Should().Throw<DomainException>();
    }
}

public class RankingTests
{
    [Fact]
    public void Accumulates_points_and_shares_positions_on_ties()
    {
        var ranking = new Ranking(Guid.CreateVersion7(), Discipline.TenBall, "2026");
        var (a, b, c) = (Guid.CreateVersion7(), Guid.CreateVersion7(), Guid.CreateVersion7());

        ranking.AwardPoints(a, 50);
        ranking.AwardPoints(b, 30);
        ranking.AwardPoints(c, 20);
        ranking.AwardPoints(c, 30);

        ranking.PointsFor(c).Should().Be(50);
        ranking.Table().Select(r => r.Position).Should().Equal(1, 1, 3);
        ranking.Table().Last().PlayerId.Should().Be(b);
        ranking.Invoking(r => r.AwardPoints(a, 0)).Should().Throw<DomainException>();
    }

    [Fact]
    public void Ratings_start_at_the_initial_value_and_count_matches()
    {
        var rating = new Rating(Guid.CreateVersion7(), Discipline.Snooker);

        rating.Value.Should().Be(Rating.Initial);
        rating.Apply(1516.5);

        rating.Value.Should().Be(1516.5);
        rating.MatchesPlayed.Should().Be(1);
        rating.Invoking(r => r.Apply(double.NaN)).Should().Throw<DomainException>();
    }
}

public class ChallengeTests
{
    private static Challenge NewChallenge(out Guid challenger, out Guid opponent)
    {
        challenger = Guid.CreateVersion7();
        opponent = Guid.CreateVersion7();
        return new Challenge(challenger, opponent, Discipline.Heyball, MatchLength.RaceTo(4));
    }

    [Fact]
    public void Runs_from_proposal_to_result()
    {
        var challenge = NewChallenge(out _, out var opponent);
        var at = new DateTimeOffset(2026, 3, 1, 20, 0, 0, TimeSpan.Zero);

        challenge.Invoking(c => c.RecordResult(4, 2, at)).Should().Throw<DomainException>("not accepted yet");
        challenge.Accept();
        challenge.Invoking(c => c.RecordResult(4, 4, at)).Should().Throw<DomainException>();
        challenge.RecordResult(3, 4, at);

        challenge.Status.Should().Be(ChallengeStatus.Completed);
        challenge.WinnerId.Should().Be(opponent);
        challenge.CompletedAt.Should().Be(at);
    }

    [Fact]
    public void Can_be_declined_only_while_proposed()
    {
        var challenge = NewChallenge(out _, out _);

        challenge.Decline();

        challenge.Status.Should().Be(ChallengeStatus.Declined);
        challenge.WinnerId.Should().BeNull();
        challenge.Invoking(c => c.Accept()).Should().Throw<DomainException>();
        challenge.Invoking(c => c.Decline()).Should().Throw<DomainException>();
    }

    [Fact]
    public void A_player_cannot_challenge_themselves()
    {
        var player = Guid.CreateVersion7();

        FluentActions.Invoking(() => new Challenge(player, player, Discipline.Heyball, MatchLength.RaceTo(4)))
            .Should().Throw<DomainException>();
    }
}
