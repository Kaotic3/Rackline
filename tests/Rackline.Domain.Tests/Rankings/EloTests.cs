using FluentAssertions;
using Rackline.Domain.Common;
using Rackline.Domain.Matches;
using Rackline.Domain.Rankings;
using Rackline.Domain.Tournaments;
using static Rackline.Domain.Tests.Rankings.RankingTestSupport;
using static Rackline.Domain.Tests.TestSupport;

namespace Rackline.Domain.Tests.Rankings;

public class EloTests
{
    private static readonly Guid Ann = Guid.CreateVersion7();
    private static readonly Guid Ben = Guid.CreateVersion7();
    private static readonly Guid Cal = Guid.CreateVersion7();

    [Fact]
    public void Ratings_start_at_1500_and_are_provisional()
    {
        var rating = new Rating(Ann, Discipline.NineBall);

        rating.Value.Should().Be(Rating.Initial);
        rating.MatchesPlayed.Should().Be(0);
        rating.IsProvisional.Should().BeTrue();
    }

    [Fact]
    public void Expected_scores_are_symmetric_and_follow_the_400_point_scale()
    {
        Elo.Expected(1500, 1500).Should().Be(0.5);
        Elo.Expected(1900, 1500).Should().BeApproximately(10.0 / 11, 1e-12);
        (Elo.Expected(1620, 1480) + Elo.Expected(1480, 1620)).Should().BeApproximately(1, 1e-12);
    }

    [Fact]
    public void Between_equal_players_the_winner_gains_half_of_K_and_the_loser_loses_it()
    {
        var winner = new Rating(Ann, Discipline.NineBall);
        var loser = new Rating(Ben, Discipline.NineBall);
        var matchId = Guid.CreateVersion7();

        var (won, lost) = Elo.Rate(winner, loser, matchId, At);

        winner.Value.Should().Be(1516);
        loser.Value.Should().Be(1484);
        winner.MatchesPlayed.Should().Be(1);
        loser.MatchesPlayed.Should().Be(1);

        won.Should().BeEquivalentTo(new { PlayerId = Ann, OpponentId = Ben, MatchId = matchId, Won = true, Before = 1500.0, After = 1516.0, At });
        lost.Should().BeEquivalentTo(new { PlayerId = Ben, OpponentId = Ann, MatchId = matchId, Won = false, Before = 1500.0, After = 1484.0, At });
    }

    [Fact]
    public void An_upset_moves_ratings_more_than_an_expected_win()
    {
        var ann = new Rating(Ann, Discipline.NineBall);
        var ben = new Rating(Ben, Discipline.NineBall);
        Elo.Rate(ann, ben, Guid.CreateVersion7(), At);

        // Ann is now the favourite, so Ben gains more for beating her than she did for beating him.
        var (upset, _) = Elo.Rate(ben, ann, Guid.CreateVersion7(), At);

        (upset.After - upset.Before).Should().BeGreaterThan(16);
        ann.Value.Should().BeLessThan(Rating.Initial);
    }

    [Fact]
    public void K_drops_once_a_rating_is_settled()
    {
        Elo.KFactor(0).Should().Be(Elo.ProvisionalK);
        Elo.KFactor(Elo.ProvisionalMatches - 1).Should().Be(Elo.ProvisionalK);
        Elo.KFactor(Elo.ProvisionalMatches).Should().Be(Elo.SettledK);

        var veteran = new Rating(Ann, Discipline.NineBall);
        var other = new Rating(Ben, Discipline.NineBall);
        for (var i = 0; i < Elo.ProvisionalMatches; i++)
        {
            // Alternate wins so both stay near 1500.
            if (i % 2 == 0)
            {
                Elo.Rate(veteran, other, Guid.CreateVersion7(), At);
            }
            else
            {
                Elo.Rate(other, veteran, Guid.CreateVersion7(), At);
            }
        }

        veteran.IsProvisional.Should().BeFalse();
        var newcomer = new Rating(Cal, Discipline.NineBall);
        var veteranBefore = veteran.Value;
        Elo.Rate(newcomer, veteran, Guid.CreateVersion7(), At);

        // The newcomer moves at the provisional K; the veteran at the settled K.
        (veteranBefore - veteran.Value).Should().BeApproximately(Elo.SettledK * Elo.Expected(veteranBefore, 1500), 1e-9);
        (newcomer.Value - 1500).Should().BeApproximately(Elo.ProvisionalK * (1 - Elo.Expected(1500, veteranBefore)), 1e-9);
    }

    [Fact]
    public void Ratings_must_be_for_different_players_in_the_same_discipline()
    {
        FluentActions.Invoking(() => Elo.Rate(new Rating(Ann, Discipline.NineBall), new Rating(Ann, Discipline.NineBall), Guid.CreateVersion7(), At))
            .Should().Throw<DomainException>();
        FluentActions.Invoking(() => Elo.Rate(new Rating(Ann, Discipline.NineBall), new Rating(Ben, Discipline.Snooker), Guid.CreateVersion7(), At))
            .Should().Throw<DomainException>();
    }

    [Fact]
    public void Replaying_a_history_matches_rating_each_result_as_it_happens()
    {
        var results = new[]
        {
            new RatedResult(Guid.CreateVersion7(), Discipline.NineBall, Ann, Ben, At),
            new RatedResult(Guid.CreateVersion7(), Discipline.NineBall, Cal, Ann, At.AddHours(1)),
            new RatedResult(Guid.CreateVersion7(), Discipline.Snooker, Ben, Cal, At.AddHours(2)),
            new RatedResult(Guid.CreateVersion7(), Discipline.NineBall, Ben, Cal, At.AddHours(3)),
        };

        var live = new Dictionary<(Guid, Discipline), Rating>();
        Rating For(Guid player, Discipline discipline) =>
            live.TryGetValue((player, discipline), out var r) ? r : live[(player, discipline)] = new Rating(player, discipline);
        foreach (var result in results)
        {
            Elo.Rate(For(result.WinnerPlayerId, result.Discipline), For(result.LoserPlayerId, result.Discipline), result.MatchId, result.At);
        }

        // Given out of order, the replay still rates oldest first.
        var (ratings, changes) = Elo.Replay(results.Reverse());

        ratings.Select(r => (r.PlayerId, r.Discipline, r.Value, r.MatchesPlayed))
            .Should().BeEquivalentTo(live.Values.Select(r => (r.PlayerId, r.Discipline, r.Value, r.MatchesPlayed)));
        changes.Should().HaveCount(8);
        changes.Select(c => c.MatchId).Distinct().Should().Equal(results.Select(r => r.MatchId));
    }

    [Fact]
    public void With_equal_K_rating_points_are_only_moved_between_players()
    {
        var results = Enumerable.Range(0, 15)
            .Select(i => new RatedResult(Guid.CreateVersion7(), Discipline.NineBall, i % 3 == 0 ? Ann : Ben, i % 3 == 0 ? Cal : Ann, At.AddMinutes(i)));

        var (ratings, _) = Elo.Replay(results);

        ratings.Sum(r => r.Value).Should().BeApproximately(3 * Rating.Initial, 1e-9);
    }

    [Fact]
    public void Only_matches_played_to_a_result_are_rated()
    {
        var tournament = Started(6, TournamentFormat.SingleElimination);
        // Seeds 1 and 2 have byes. Award one first-round match as a walkover and play the other.
        var walkover = tournament.Matches.First(m => m.State == MatchState.Ready);
        tournament.AwardWalkover(walkover.Number, walkover.Slot1.EntryId!.Value, At);
        var played = PlayNext(tournament, slot1Wins: false, At.AddMinutes(30))!;

        var rated = RatedResult.From(tournament).ToList();

        rated.Should().ContainSingle();
        var players = tournament.Entries.ToDictionary(e => e.Id, e => e.PlayerId);
        rated[0].Should().Be(new RatedResult(
            played.Id, Discipline.NineBall, players[played.Slot2.EntryId!.Value], players[played.Slot1.EntryId!.Value], At.AddMinutes(30)));
    }
}
