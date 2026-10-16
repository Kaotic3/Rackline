using FluentAssertions;
using Rackline.Domain.Common;
using Rackline.Domain.Draws;
using Rackline.Domain.Matches;
using static Rackline.Domain.Tests.TestSupport;

namespace Rackline.Domain.Tests.Draws;

public class DoubleEliminationEngineTests
{
    public static IEnumerable<object[]> AllFieldSizesWithResetOption =>
        Enumerable.Range(2, 127).SelectMany(n => new[] { new object[] { n, true }, [n, false] });

    private static Bracket Draw(IReadOnlyList<Guid> entries, bool reset = true) =>
        new(new DoubleEliminationEngine(reset).Generate(TournamentId, entries, RaceTo5));

    private static Match GrandFinal(Bracket bracket) =>
        bracket.Matches.Single(m => m.Side == BracketSide.GrandFinal && m.Round == 1);

    private static Match? Reset(Bracket bracket) =>
        bracket.Matches.SingleOrDefault(m => m.Side == BracketSide.GrandFinal && m.Round == 2);

    [Theory]
    [MemberData(nameof(AllFieldSizesWithResetOption))]
    public void Builds_winners_losers_and_grand_final_matches(int n, bool reset)
    {
        var size = SeedPlacement.BracketSize(n);
        var k = (int)Math.Log2(size);
        var matches = Draw(Entries(n), reset).Matches.ToList();

        matches.Count(m => m.Side == BracketSide.Winners).Should().Be(size - 1);
        matches.Count(m => m.Side == BracketSide.Losers).Should().Be(size - 2);
        matches.Count(m => m.Side == BracketSide.GrandFinal).Should().Be(reset ? 2 : 1);
        matches.Select(m => m.Number).Should().BeEquivalentTo(Enumerable.Range(1, matches.Count));

        var losersRounds = matches.Where(m => m.Side == BracketSide.Losers).Select(m => m.Round).Distinct();
        losersRounds.Should().HaveCount(2 * (k - 1));
    }

    [Theory]
    [MemberData(nameof(AllFieldSizesWithResetOption))]
    public void Better_seeds_winning_crowns_seed_one_and_needs_no_reset(int n, bool reset)
    {
        var entries = Entries(n);
        var bracket = Draw(entries, reset);

        PlayOut(bracket, BetterSeedWins(entries));

        bracket.IsFinished.Should().BeTrue();
        bracket.ChampionEntryId.Should().Be(entries[0]);
        GrandFinal(bracket).Slot1.EntryId.Should().Be(entries[0]);
        GrandFinal(bracket).Slot2.EntryId.Should().Be(entries[1], "seed 2 loses only to seed 1, then wins the losers' bracket");

        if (reset)
        {
            Reset(bracket)!.IsVoid.Should().BeTrue();
        }

        AssertEliminatedEntriesLostTwice(bracket, entries, reset);
    }

    [Theory]
    [MemberData(nameof(AllFieldSizesWithResetOption))]
    public void Random_results_always_finish_with_a_consistent_bracket(int n, bool reset)
    {
        var entries = Entries(n);
        var bracket = Draw(entries, reset);
        var random = new SeededRandom(n * 31 + (reset ? 1 : 0));

        PlayOut(bracket, _ => random.Next(2) == 0);

        bracket.IsFinished.Should().BeTrue();
        bracket.ChampionEntryId.Should().NotBeNull();
        AssertEliminatedEntriesLostTwice(bracket, entries, reset);
    }

    [Theory]
    [MemberData(nameof(AllFieldSizesWithResetOption))]
    public void Every_bye_resolves_without_leaving_a_match_stuck(int n, bool reset)
    {
        var bracket = Draw(Entries(n), reset);

        // Before any match is played, every match is either waiting on a result, ready, or settled by a bye.
        bracket.Matches.Should().OnlyContain(m =>
            m.State == MatchState.Pending || m.State == MatchState.Ready || m.IsBye || m.IsVoid);

        // Every pending match must still be able to receive something in each awaiting slot.
        var feeds = bracket.Matches
            .SelectMany(m => new[] { m.WinnerTo, m.LoserTo }.Where(l => l is not null && !m.IsResolved))
            .ToList();
        foreach (var match in bracket.Matches.Where(m => m.State == MatchState.Pending && m.Side != BracketSide.GrandFinal))
        {
            for (var slot = 1; slot <= 2; slot++)
            {
                if (match.GetSlot(slot).IsAwaiting)
                {
                    feeds.Should().Contain(new SlotLink(match.Number, slot));
                }
            }
        }
    }

    [Fact]
    public void Two_players_play_a_final_and_then_a_grand_final()
    {
        var entries = Entries(2);
        var bracket = Draw(entries, reset: false);

        bracket.Matches.Should().HaveCount(2);
        bracket.RecordResult(1, 5, 2, At);

        var grandFinal = GrandFinal(bracket);
        grandFinal.State.Should().Be(MatchState.Ready);
        grandFinal.Slot1.EntryId.Should().Be(entries[0]);
        grandFinal.Slot2.EntryId.Should().Be(entries[1]);

        bracket.RecordResult(grandFinal.Number, 3, 5, At);
        bracket.ChampionEntryId.Should().Be(entries[1]);
    }

    [Fact]
    public void Losers_bracket_champion_winning_the_grand_final_forces_a_reset()
    {
        var entries = Entries(4);
        var bracket = Draw(entries);

        // Slot 1 wins everything except the grand finals, which the losers' bracket champion takes.
        PlayOut(bracket, m => m.Side != BracketSide.GrandFinal);

        var grandFinal = GrandFinal(bracket);
        var reset = Reset(bracket)!;

        grandFinal.State.Should().Be(MatchState.Completed);
        grandFinal.WinnerEntryId.Should().Be(grandFinal.Slot2.EntryId);
        reset.Slot1.EntryId.Should().Be(grandFinal.Slot1.EntryId);
        reset.Slot2.EntryId.Should().Be(grandFinal.Slot2.EntryId);
        reset.State.Should().Be(MatchState.Completed);
        bracket.ChampionEntryId.Should().Be(reset.WinnerEntryId);
    }

    [Fact]
    public void Without_a_reset_the_losers_bracket_champion_wins_outright()
    {
        var entries = Entries(4);
        var bracket = Draw(entries, reset: false);

        PlayOut(bracket, m => m.Side != BracketSide.GrandFinal);

        var grandFinal = GrandFinal(bracket);
        bracket.IsFinished.Should().BeTrue();
        bracket.ChampionEntryId.Should().Be(grandFinal.Slot2.EntryId);
    }

    [Fact]
    public void Losers_of_the_first_round_drop_into_the_losers_bracket_in_pairs()
    {
        var bracket = Draw(Entries(8));
        var winnersRound1 = bracket.Matches.Where(m => m.Side == BracketSide.Winners && m.Round == 1).OrderBy(m => m.Position).ToList();
        var losersRound1 = bracket.Matches.Where(m => m.Side == BracketSide.Losers && m.Round == 1).OrderBy(m => m.Position).ToList();

        winnersRound1.Select(m => m.LoserTo).Should().Equal(
            new SlotLink(losersRound1[0].Number, 1),
            new SlotLink(losersRound1[0].Number, 2),
            new SlotLink(losersRound1[1].Number, 1),
            new SlotLink(losersRound1[1].Number, 2));

        bracket.RecordResult(winnersRound1[0].Number, 5, 0, At);
        losersRound1[0].Slot1.EntryId.Should().Be(winnersRound1[0].Slot2.EntryId);
    }

    [Theory]
    [InlineData(8)]
    [InlineData(16)]
    [InlineData(32)]
    [InlineData(64)]
    [InlineData(128)]
    public void Second_round_drop_ins_avoid_an_immediate_rematch(int n)
    {
        var bracket = Draw(Entries(n));
        var matches = bracket.Matches.ToList();

        IEnumerable<int> WinnersRound1Feeders(Match target, Func<Match, SlotLink?> link) =>
            matches.Where(m => link(m)?.MatchNumber == target.Number).Select(m => m.Number);

        foreach (var drop in matches.Where(m => m.Side == BracketSide.Losers && m.Round == 2))
        {
            // Slot 1: survivors of a losers' round 1 match, who lost in two specific winners' round 1 matches.
            var losersRound1 = matches.Single(m => m.WinnerTo == new SlotLink(drop.Number, 1));
            var survivorsCameFrom = WinnersRound1Feeders(losersRound1, m => m.LoserTo).ToHashSet();

            // Slot 2: the loser of a winners' round 2 match, whose players came from two winners' round 1 matches.
            var winnersRound2 = matches.Single(m => m.LoserTo == new SlotLink(drop.Number, 2));
            var dropInCameFrom = WinnersRound1Feeders(winnersRound2, m => m.WinnerTo).ToHashSet();

            survivorsCameFrom.Should().NotIntersectWith(dropInCameFrom);
        }
    }

    [Fact]
    public void Byes_cascade_through_the_losers_bracket()
    {
        // 5 entries in an 8-line bracket: three first-round byes, so the losers' bracket starts mostly empty.
        var entries = Entries(5);
        var bracket = Draw(entries);
        var losersRound1 = bracket.Matches.Where(m => m.Side == BracketSide.Losers && m.Round == 1).ToList();

        losersRound1.Should().ContainSingle(m => m.IsVoid, "two first-round byes feed the same losers' match");
        losersRound1.Should().ContainSingle(m => m.State == MatchState.Pending);
    }

    [Fact]
    public void A_walkover_loser_still_drops_into_the_losers_bracket()
    {
        var entries = Entries(4);
        var bracket = Draw(entries);
        var opener = bracket.Matches.Single(m => m.Side == BracketSide.Winners && m.Round == 1 && m.Position == 1);

        bracket.AwardWalkover(opener.Number, entries[0], At);

        var target = bracket[opener.LoserTo!.MatchNumber];
        target.GetSlot(opener.LoserTo.Slot).EntryId.Should().Be(entries[3]);
    }

    [Fact]
    public void Generation_is_deterministic()
    {
        var entries = Entries(23);
        var engine = new DoubleEliminationEngine(grandFinalReset: true);

        var first = engine.Generate(TournamentId, entries, RaceTo5);
        var second = engine.Generate(TournamentId, entries, RaceTo5);

        first.Select(Describe).Should().Equal(second.Select(Describe));
    }

    private static void AssertEliminatedEntriesLostTwice(Bracket bracket, IReadOnlyList<Guid> entries, bool reset)
    {
        var champion = bracket.ChampionEntryId!.Value;
        var losses = LossCounts(bracket.Matches);
        var grandFinal = GrandFinal(bracket);

        // Without a reset, an unbeaten winners' bracket champion who loses the grand final is out after one loss.
        Guid? oneLossRunnerUp = !reset && grandFinal.WinnerEntryId == grandFinal.Slot2.EntryId
            ? grandFinal.Slot1.EntryId
            : null;

        losses.GetValueOrDefault(champion).Should().BeLessThan(2);
        foreach (var entry in entries.Where(e => e != champion))
        {
            losses.GetValueOrDefault(entry).Should().Be(entry == oneLossRunnerUp ? 1 : 2, "an eliminated entry has lost twice");
        }
    }
}
