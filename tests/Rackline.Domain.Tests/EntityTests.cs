using FluentAssertions;
using Rackline.Domain.Challenges;
using Rackline.Domain.Common;
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
