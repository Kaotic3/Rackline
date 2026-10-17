using FluentAssertions;
using Rackline.Domain.Challenges;
using Rackline.Domain.Common;
using Rackline.Domain.Rankings;
using static Rackline.Domain.Tests.TestSupport;

namespace Rackline.Domain.Tests.Challenges;

public class ChallengeTests
{
    private static readonly Guid Club = Guid.CreateVersion7();
    private static readonly Guid Ann = Guid.CreateVersion7();
    private static readonly Guid Ben = Guid.CreateVersion7();

    /// <summary>
    /// A settled 9-ball rating for the player, built from a real history of invented results:
    /// <paramref name="wins"/> wins and the rest losses, out of the matches needed to settle.
    /// </summary>
    private static Rating Settled(Guid player, int wins)
    {
        var results = Enumerable.Range(0, Elo.ProvisionalMatches).Select(i =>
        {
            var opponent = Guid.CreateVersion7();
            return i < wins
                ? new RatedResult(Guid.CreateVersion7(), Discipline.NineBall, player, opponent, At.AddDays(-100 + i))
                : new RatedResult(Guid.CreateVersion7(), Discipline.NineBall, opponent, player, At.AddDays(-100 + i));
        });

        return Elo.Replay(results).Ratings.Single(r => r.PlayerId == player);
    }

    private static Challenge Record(int score1, int score2, ChallengeAssessment? assessment = null) =>
        new(Club, Ann, Ben, Discipline.NineBall, MatchLength.RaceTo(5), score1, score2, At, assessment ?? ChallengeAssessment.Rated);

    [Fact]
    public void Two_settled_players_with_close_ratings_count()
    {
        var ann = Settled(Ann, wins: 11);
        var ben = Settled(Ben, wins: 9);

        ann.IsProvisional.Should().BeFalse();
        Math.Abs(ann.Value - ben.Value).Should().BeLessThan(ChallengeRules.MaxRatingGap);
        ChallengeRules.Assess(ann, ben, At, []).Should().Be(ChallengeAssessment.Rated);
    }

    [Fact]
    public void A_player_with_a_provisional_or_no_rating_does_not_count()
    {
        var settled = Settled(Ann, wins: 10);
        var provisional = Elo.Replay([new RatedResult(Guid.CreateVersion7(), Discipline.NineBall, Ben, Guid.CreateVersion7(), At)]).Ratings
            .Single(r => r.PlayerId == Ben);

        var withProvisional = ChallengeRules.Assess(settled, provisional, At, []);
        var withNone = ChallengeRules.Assess(null, settled, At, []);

        withProvisional.Counts.Should().BeFalse();
        withProvisional.Reasons.Should().ContainSingle().Which.Should().Contain("settled rating");
        withNone.Counts.Should().BeFalse();
    }

    [Fact]
    public void A_strong_player_beating_a_much_weaker_one_does_not_count()
    {
        var strong = Settled(Ann, wins: 20);
        var weak = Settled(Ben, wins: 0);

        var assessment = ChallengeRules.Assess(strong, weak, At, []);

        (strong.Value - weak.Value).Should().BeGreaterThan(ChallengeRules.MaxRatingGap);
        assessment.Counts.Should().BeFalse();
        assessment.Reasons.Should().ContainSingle().Which.Should().Contain("points apart").And.Contain("within 200");
    }

    [Theory]
    [InlineData(-29, false)]
    [InlineData(-31, true)]
    [InlineData(10, false)]
    public void The_same_pair_counts_once_in_thirty_days_either_side(int daysFromThisOne, bool counts)
    {
        var ann = Settled(Ann, wins: 10);
        var ben = Settled(Ben, wins: 10);

        var assessment = ChallengeRules.Assess(ann, ben, At, [At.AddDays(daysFromThisOne)]);

        assessment.Counts.Should().Be(counts);
        if (!counts)
        {
            assessment.Reasons.Should().ContainSingle().Which.Should().Contain("30 days");
        }
    }

    [Fact]
    public void Every_reason_is_given()
    {
        var assessment = ChallengeRules.Assess(null, null, At, [At]);

        assessment.Reasons.Should().HaveCount(2);
    }

    [Fact]
    public void A_recorded_challenge_keeps_its_result_and_whether_it_counts()
    {
        var rated = Record(3, 5);
        rated.WinnerId.Should().Be(Ben);
        rated.LoserId.Should().Be(Ann);
        rated.ToRatedResult().Should().Be(new RatedResult(rated.Id, Discipline.NineBall, Ben, Ann, At));

        var unrated = Record(5, 1, new ChallengeAssessment(false, ["First reason.", "Second reason."]));
        unrated.Rated.Should().BeFalse();
        unrated.UnratedReason.Should().Be("First reason. Second reason.");
        unrated.ToRatedResult().Should().BeNull();
    }

    [Theory]
    [InlineData(5, 5)]
    [InlineData(4, 3)]
    [InlineData(6, 2)]
    [InlineData(-1, 5)]
    public void Exactly_one_player_must_reach_the_target(int score1, int score2)
    {
        FluentActions.Invoking(() => Record(score1, score2)).Should().Throw<DomainException>();
    }

    [Fact]
    public void A_player_cannot_play_themselves()
    {
        FluentActions.Invoking(() => new Challenge(Club, Ann, Ann, Discipline.NineBall, MatchLength.RaceTo(5), 5, 0, At, ChallengeAssessment.Rated))
            .Should().Throw<DomainException>();
    }
}
