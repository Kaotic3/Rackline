using FluentAssertions;
using Rackline.Domain.Common;
using Rackline.Domain.Matches;
using Rackline.Domain.Standings;
using Rackline.Domain.Tournaments;
using static Rackline.Domain.Tests.Rankings.RankingTestSupport;
using static Rackline.Domain.Tests.TestSupport;

namespace Rackline.Domain.Tests.Rankings;

public class FinishingPositionsTests
{
    public static IEnumerable<object[]> Sizes => FieldSizes(2, 64);

    private static int[] PositionsBySeed(Tournament tournament)
    {
        var positions = FinishingPositions.Calculate(tournament).ToDictionary(p => p.EntryId, p => p.Position);
        return [.. BySeed(tournament).Select(id => positions[id])];
    }

    /// <summary>When the better seed always wins, seed s goes out in the round of the top 2^k seeds above it.</summary>
    private static int ExpectedKnockoutPosition(int seed) =>
        seed <= 2 ? seed : (1 << (int)Math.Ceiling(Math.Log2(seed)) - 1) + 1;

    [Theory]
    [MemberData(nameof(Sizes))]
    public void Single_elimination_places_players_by_the_round_they_went_out(int count)
    {
        var tournament = PlayedOut(count, TournamentFormat.SingleElimination);

        PositionsBySeed(tournament).Should().Equal(Enumerable.Range(1, count).Select(ExpectedKnockoutPosition));
    }

    [Theory]
    [MemberData(nameof(Sizes))]
    public void Double_elimination_gives_everyone_a_position_and_better_seeds_never_finish_lower(int count)
    {
        var tournament = PlayedOut(count, TournamentFormat.DoubleElimination);
        var positions = PositionsBySeed(tournament);

        positions.Should().HaveCount(count);
        positions[0].Should().Be(1);
        positions[1].Should().Be(2);
        positions.Should().BeInAscendingOrder();
        positions.Should().OnlyContain(p => p == 1 + positions.Count(other => other < p), "a shared position leaves a gap after it");
    }

    [Fact]
    public void Double_elimination_of_sixteen_places_by_the_losers_round()
    {
        var tournament = PlayedOut(16, TournamentFormat.DoubleElimination);

        PositionsBySeed(tournament).Should().Equal(1, 2, 3, 4, 5, 5, 7, 7, 9, 9, 9, 9, 13, 13, 13, 13);
    }

    [Fact]
    public void A_grand_final_reset_decides_first_and_second()
    {
        var tournament = Started(4, TournamentFormat.DoubleElimination);
        var seeds = BySeed(tournament);
        var betterSeedWins = BetterSeedWins(seeds);

        // Everything goes to seed until the grand final, which the losers' bracket champion wins twice.
        while (tournament.Matches.Where(m => m.State == MatchState.Ready).MinBy(m => m.Number) is { } match)
        {
            var slot1Wins = match.Side == BracketSide.GrandFinal ? false : betterSeedWins(match);
            PlayNext(tournament, slot1Wins, At);
        }

        tournament.Complete();
        PositionsBySeed(tournament).Should().Equal(2, 1, 3, 4);
    }

    [Fact]
    public void A_walkover_puts_the_loser_out_in_that_round()
    {
        var tournament = Started(4, TournamentFormat.SingleElimination);
        var seeds = BySeed(tournament);
        var firstMatch = tournament.Matches.First(m => m.Round == 1 && m.Involves(seeds[0]));
        tournament.AwardWalkover(firstMatch.Number, seeds[3], At);
        PlayOut(tournament.Bracket, BetterSeedWins(seeds));
        tournament.Complete();

        PositionsBySeed(tournament).Should().Equal(3, 1, 3, 2);
    }

    [Fact]
    public void A_round_robin_places_players_by_the_table()
    {
        var tournament = PlayedOut(5, TournamentFormat.RoundRobin);

        PositionsBySeed(tournament).Should().Equal(1, 2, 3, 4, 5);
    }

    [Fact]
    public void Several_groups_place_players_by_their_position_in_their_group()
    {
        var tournament = PlayedOut(8, TournamentFormat.RoundRobin, new FormatOptions(GroupCount: 2));

        PositionsBySeed(tournament).Order().Should().Equal(1, 1, 3, 3, 5, 5, 7, 7);
    }

    [Fact]
    public void Positions_need_a_finished_draw()
    {
        var tournament = Started(4, TournamentFormat.SingleElimination);

        FluentActions.Invoking(() => FinishingPositions.Calculate(tournament)).Should().Throw<DomainException>();
    }
}
