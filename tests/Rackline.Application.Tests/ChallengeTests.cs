using FluentAssertions;
using Rackline.Application.Challenges;
using Rackline.Application.Common;
using Rackline.Application.Rankings;
using Rackline.Domain.Common;
using Rackline.Domain.Organisations;
using Rackline.Domain.Players;
using Rackline.Domain.Rankings;

namespace Rackline.Application.Tests;

/// <summary>Recording challenges as a club referee, and how they count towards ratings.</summary>
public class ChallengeTests
{
    private static readonly DateTimeOffset Now = new(2026, 11, 1, 20, 0, 0, TimeSpan.Zero);

    private readonly Organisation _region = new("Lowland Region", OrganisationLevel.Regional);
    private readonly Organisation _club;
    private readonly Organisation _otherClub;
    private readonly Player _ann = new("Ann", "Example", "GB");
    private readonly Player _ben = new("Ben", "Example", "GB");

    private readonly FakeUser _user = new() { IsAdmin = false };
    private readonly FakeDirectory _directory = new();
    private readonly InMemoryChallenges _challenges = new();
    private readonly InMemoryPlayers _players = new();
    private readonly InMemoryRatings _ratings = new();
    private readonly FixedTime _time = new(Now);

    public ChallengeTests()
    {
        _club = new Organisation("Lowland Cue Club", OrganisationLevel.Club, _region);
        _otherClub = new Organisation("Upland Cue Club", OrganisationLevel.Club, _region);
        foreach (var organisation in new[] { _region, _club, _otherClub })
        {
            _directory.Parents[organisation.Id] = organisation.ParentId;
        }

        _ann.JoinOrganisation(_club.Id);
        _ben.JoinOrganisation(_club.Id);
        _players.Players.AddRange([_ann, _ben]);
        _directory.Appointments.Add(new Appointment(_club.Id, _user.UserId!.Value, StaffRole.Referee));
    }

    private ChallengeRecording Recording => new(_challenges, _players, _ratings, new OrganisationAccess(_user, _directory), new RatingRebuild(_ratings), _time);

    /// <summary>Gives the player a settled 9-ball rating from invented history: <paramref name="wins"/> wins, the rest losses.</summary>
    private void Settle(Player player, int wins)
    {
        var history = Enumerable.Range(0, Elo.ProvisionalMatches).Select(i =>
        {
            var opponent = Guid.CreateVersion7();
            return i < wins
                ? new RatedResult(Guid.CreateVersion7(), Discipline.NineBall, player.Id, opponent, Now.AddDays(-200 + i))
                : new RatedResult(Guid.CreateVersion7(), Discipline.NineBall, opponent, player.Id, Now.AddDays(-200 + i));
        });
        _ratings.Ratings.Add(Elo.Replay(history).Ratings.Single(r => r.PlayerId == player.Id));
    }

    private Task<Guid> RecordAsync(int score1 = 5, int score2 = 3, Guid? organisationId = null) =>
        Recording.RecordAsync(organisationId ?? _club.Id, _ann.Id, _ben.Id, Discipline.NineBall, MatchLengthKind.RaceTo, 5, score1, score2);

    [Fact]
    public async Task A_challenge_between_settled_and_close_players_moves_both_ratings()
    {
        Settle(_ann, wins: 10);
        Settle(_ben, wins: 11);
        var before = _ratings.For(_ann.Id, Discipline.NineBall)!.Value;

        var id = await RecordAsync(5, 3);

        var challenge = _challenges.Challenges.Single();
        challenge.Should().BeEquivalentTo(new { Id = id, Rated = true, UnratedReason = (string?)null, PlayedAt = Now }, o => o.ExcludingMissingMembers());
        _ratings.For(_ann.Id, Discipline.NineBall)!.Value.Should().BeGreaterThan(before);
        _ratings.Changes.Should().HaveCount(2).And.OnlyContain(c => c.MatchId == id);
        _challenges.SaveCount.Should().Be(1);
    }

    [Fact]
    public async Task A_challenge_against_a_provisional_player_is_kept_but_moves_no_rating()
    {
        Settle(_ann, wins: 10);

        await RecordAsync();

        var challenge = _challenges.Challenges.Single();
        challenge.Rated.Should().BeFalse();
        challenge.UnratedReason.Should().Contain("settled rating");
        _ratings.Changes.Should().BeEmpty();
        _ratings.Ratings.Should().ContainSingle("no rating is created for the unrated player");
    }

    [Fact]
    public async Task A_strong_player_cannot_farm_a_weak_one()
    {
        Settle(_ann, wins: 20);
        Settle(_ben, wins: 0);

        (await Recording.PreviewAsync(_ann.Id, _ben.Id, Discipline.NineBall)).Counts.Should().BeFalse();
        await RecordAsync(5, 0);

        _challenges.Challenges.Single().UnratedReason.Should().Contain("points apart");
        _ratings.Changes.Should().BeEmpty();
    }

    [Fact]
    public async Task The_same_pair_counts_only_once_a_month()
    {
        Settle(_ann, wins: 10);
        Settle(_ben, wins: 10);

        await RecordAsync(5, 3);
        _time.Now = Now.AddDays(10);
        await RecordAsync(3, 5);
        _time.Now = Now.AddDays(31);
        await RecordAsync(5, 4);

        _challenges.Challenges.Select(c => c.Rated).Should().Equal(true, false, true);
        _challenges.Challenges[1].UnratedReason.Should().Contain("30 days");
        _ratings.Changes.Should().HaveCount(4);
    }

    [Fact]
    public async Task Only_staff_of_the_players_organisation_can_record_and_both_players_must_belong()
    {
        // A referee at another club cannot record a challenge for this club.
        _directory.Appointments.Clear();
        _directory.Appointments.Add(new Appointment(_otherClub.Id, _user.UserId!.Value, StaffRole.Referee));
        await FluentActions.Awaiting(() => RecordAsync()).Should().ThrowAsync<ForbiddenException>();

        // A referee at the region can record it for the club below.
        _directory.Appointments.Clear();
        _directory.Appointments.Add(new Appointment(_region.Id, _user.UserId!.Value, StaffRole.Referee));
        await RecordAsync(organisationId: _region.Id);

        // But both players must belong to the organisation recording it, or one below. Ben has moved
        // to another club, so the club (where the regional referee may also act) cannot record it.
        _ben.LeaveOrganisation(_club.Id);
        _ben.JoinOrganisation(_otherClub.Id);
        await FluentActions.Awaiting(() => RecordAsync(organisationId: _club.Id)).Should().ThrowAsync<DomainException>()
            .WithMessage("*Ben Example is not a member*");
    }

    [Fact]
    public async Task Removing_a_rated_challenge_takes_it_out_of_everyone_s_rating()
    {
        Settle(_ann, wins: 10);
        Settle(_ben, wins: 10);
        var id = await RecordAsync();

        _directory.Appointments.Add(new Appointment(_club.Id, _user.UserId!.Value, StaffRole.Referee));
        await FluentActions.Awaiting(() => Recording.DeleteAsync(id)).Should().ThrowAsync<ForbiddenException>("only organisers remove results");

        _directory.Appointments.Clear();
        _directory.Appointments.Add(new Appointment(_club.Id, _user.UserId!.Value, StaffRole.Organiser));
        await Recording.DeleteAsync(id);

        _challenges.Challenges.Should().BeEmpty();
        _ratings.Changes.Should().BeEmpty("the rebuild replays history without the challenge (the fake history is empty)");
        await FluentActions.Awaiting(() => Recording.DeleteAsync(id)).Should().ThrowAsync<NotFoundException>();
    }

    [Fact]
    public async Task Unknown_players_are_not_found()
    {
        await FluentActions.Awaiting(() => Recording.PreviewAsync(_ann.Id, Guid.CreateVersion7(), Discipline.NineBall)).Should().ThrowAsync<NotFoundException>();
        await FluentActions.Awaiting(() => Recording.RecordAsync(_club.Id, _ann.Id, Guid.CreateVersion7(), Discipline.NineBall, MatchLengthKind.RaceTo, 5, 5, 0))
            .Should().ThrowAsync<NotFoundException>();
    }
}
