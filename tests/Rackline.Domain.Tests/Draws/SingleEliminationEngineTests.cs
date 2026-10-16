using FluentAssertions;
using Rackline.Domain.Common;
using Rackline.Domain.Draws;
using Rackline.Domain.Matches;
using static Rackline.Domain.Tests.TestSupport;

namespace Rackline.Domain.Tests.Draws;

public class SingleEliminationEngineTests
{
    private readonly SingleEliminationEngine _engine = new();

    public static IEnumerable<object[]> AllFieldSizes => FieldSizes(2, 128);

    private Bracket Draw(IReadOnlyList<Guid> entries) => new(_engine.Generate(TournamentId, entries, RaceTo5));

    [Theory]
    [MemberData(nameof(AllFieldSizes))]
    public void Builds_a_full_bracket_with_numbered_rounds(int n)
    {
        var size = SeedPlacement.BracketSize(n);
        var matches = _engine.Generate(TournamentId, Entries(n), RaceTo5);

        matches.Should().HaveCount(size - 1);
        matches.Select(m => m.Number).Should().Equal(Enumerable.Range(1, size - 1));
        matches.Should().OnlyContain(m => m.Side == BracketSide.Winners && m.TournamentId == TournamentId && m.FramesToWin == 5);

        var rounds = (int)Math.Log2(size);
        for (var r = 1; r <= rounds; r++)
        {
            matches.Where(m => m.Round == r).Should().HaveCount(size >> r);
        }

        var final = matches.Single(m => m.WinnerTo is null);
        final.Round.Should().Be(rounds);
    }

    [Theory]
    [MemberData(nameof(AllFieldSizes))]
    public void Places_every_entry_once_and_gives_byes_to_the_top_seeds(int n)
    {
        var entries = Entries(n);
        var size = SeedPlacement.BracketSize(n);
        var firstRound = Draw(entries).Matches.Where(m => m.Round == 1).ToList();

        var placed = firstRound.SelectMany(m => new[] { m.Slot1, m.Slot2 }).Where(s => s.HasEntry).Select(s => s.EntryId!.Value);
        placed.Order().Should().Equal(entries.Order());

        var byes = firstRound.Where(m => m.IsBye).ToList();
        byes.Should().HaveCount(size - n);
        firstRound.Should().NotContain(m => m.IsVoid, "two byes never meet");

        var byeWinners = byes.Select(m => m.WinnerEntryId!.Value);
        byeWinners.Order().Should().Equal(entries.Take(size - n).Order(), "byes go to the highest seeds");

        firstRound.Where(m => !m.IsBye).Should().OnlyContain(m => m.State == MatchState.Ready);
    }

    [Theory]
    [MemberData(nameof(AllFieldSizes))]
    public void Seeds_one_and_two_are_in_opposite_halves(int n)
    {
        var entries = Entries(n);
        var size = SeedPlacement.BracketSize(n);
        var firstRound = Draw(entries).Matches.Where(m => m.Round == 1).ToList();

        if (size == 2)
        {
            firstRound.Single().Involves(entries[0]).Should().BeTrue();
            firstRound.Single().Involves(entries[1]).Should().BeTrue();
            return;
        }

        var matchesPerHalf = size / 4;
        int HalfOf(Guid entry) => (firstRound.Single(m => m.Involves(entry)).Position - 1) / matchesPerHalf;

        HalfOf(entries[0]).Should().Be(0);
        HalfOf(entries[1]).Should().Be(1);
    }

    [Theory]
    [MemberData(nameof(AllFieldSizes))]
    public void Better_seeds_winning_leaves_seed_one_beating_seed_two_in_the_final(int n)
    {
        var entries = Entries(n);
        var bracket = Draw(entries);

        PlayOut(bracket, BetterSeedWins(entries));

        bracket.IsFinished.Should().BeTrue();
        bracket.ChampionEntryId.Should().Be(entries[0]);

        var final = bracket.Matches.Single(m => m.WinnerTo is null);
        final.Slot1.EntryId.Should().Be(entries[0]);
        final.Slot2.EntryId.Should().Be(entries[1]);
    }

    [Theory]
    [MemberData(nameof(AllFieldSizes))]
    public void Everyone_but_the_champion_loses_exactly_once(int n)
    {
        var entries = Entries(n);
        var bracket = Draw(entries);
        var random = new SeededRandom(n);

        PlayOut(bracket, _ => random.Next(2) == 0);

        var losses = LossCounts(bracket.Matches);
        losses.Keys.Order().Should().Equal(entries.Except([bracket.ChampionEntryId!.Value]).Order());
        losses.Values.Should().OnlyContain(c => c == 1);
    }

    [Fact]
    public void Winners_move_into_the_correct_slot_of_the_next_match()
    {
        var entries = Entries(8);
        var bracket = Draw(entries);
        var r1 = bracket.Matches.Where(m => m.Round == 1).OrderBy(m => m.Position).ToList();
        var semi1 = bracket.Matches.Single(m => m.Round == 2 && m.Position == 1);

        semi1.State.Should().Be(MatchState.Pending);

        bracket.RecordResult(r1[0].Number, 5, 3, At);
        semi1.Slot1.EntryId.Should().Be(r1[0].Slot1.EntryId);
        semi1.Slot2.IsAwaiting.Should().BeTrue();
        semi1.State.Should().Be(MatchState.Pending);

        bracket.RecordResult(r1[1].Number, 2, 5, At);
        semi1.Slot2.EntryId.Should().Be(r1[1].Slot2.EntryId);
        semi1.State.Should().Be(MatchState.Ready);
    }

    [Fact]
    public void A_bye_sends_the_top_seed_straight_through()
    {
        var entries = Entries(3);
        var bracket = Draw(entries);
        var final = bracket.Matches.Single(m => m.Round == 2);

        final.Slot1.EntryId.Should().Be(entries[0]);
        final.State.Should().Be(MatchState.Pending);
    }

    [Fact]
    public void A_walkover_advances_the_awarded_entry()
    {
        var entries = Entries(4);
        var bracket = Draw(entries);
        var opener = bracket.Matches.Single(m => m.Round == 1 && m.Position == 1);
        var final = bracket.Matches.Single(m => m.Round == 2);

        bracket.AwardWalkover(opener.Number, entries[3], At);

        opener.State.Should().Be(MatchState.Walkover);
        opener.IsBye.Should().BeFalse();
        opener.WinnerEntryId.Should().Be(entries[3]);
        opener.LoserEntryId.Should().Be(entries[0]);
        final.Slot1.EntryId.Should().Be(entries[3]);
    }

    [Fact]
    public void A_walkover_must_go_to_someone_in_the_match()
    {
        var bracket = Draw(Entries(4));

        bracket.Invoking(b => b.AwardWalkover(1, Guid.NewGuid(), At)).Should().Throw<DomainException>();
    }

    [Fact]
    public void Results_cannot_be_recorded_for_a_pending_match()
    {
        var bracket = Draw(Entries(4));
        var final = bracket.Matches.Single(m => m.Round == 2);

        bracket.Invoking(b => b.RecordResult(final.Number, 5, 0, At)).Should().Throw<DomainException>();
    }

    [Theory]
    [InlineData(5, 5)]
    [InlineData(4, 3)]
    [InlineData(6, 2)]
    [InlineData(-1, 5)]
    public void Invalid_scores_are_rejected(int score1, int score2)
    {
        var bracket = Draw(Entries(2));

        bracket.Invoking(b => b.RecordResult(1, score1, score2, At)).Should().Throw<DomainException>();
        bracket[1].State.Should().Be(MatchState.Ready);
    }

    [Fact]
    public void A_finished_match_cannot_be_completed_again()
    {
        var bracket = Draw(Entries(2));
        bracket.RecordResult(1, 5, 1, At);

        bracket.Invoking(b => b.RecordResult(1, 5, 2, At)).Should().Throw<DomainException>();
        bracket.Invoking(b => b.AwardWalkover(1, bracket[1].Slot1.EntryId!.Value, At)).Should().Throw<DomainException>();
    }

    [Fact]
    public void Unknown_match_numbers_are_rejected()
    {
        var bracket = Draw(Entries(2));

        bracket.Invoking(b => b[99]).Should().Throw<DomainException>();
    }

    [Fact]
    public void Champion_is_unknown_until_the_final_is_played()
    {
        var entries = Entries(4);
        var bracket = Draw(entries);
        bracket.RecordResult(1, 5, 0, At);
        bracket.RecordResult(2, 5, 0, At);

        bracket.ChampionEntryId.Should().BeNull();

        bracket.RecordResult(3, 5, 4, At);
        bracket.ChampionEntryId.Should().Be(entries[0]);
    }

    [Fact]
    public void Generation_is_deterministic()
    {
        var entries = Entries(37);

        var first = _engine.Generate(TournamentId, entries, RaceTo5);
        var second = _engine.Generate(TournamentId, entries, RaceTo5);

        first.Select(Describe).Should().Equal(second.Select(Describe));
    }

    [Fact]
    public void Rejects_too_few_or_duplicate_entries()
    {
        var one = Entries(1);
        var duplicate = new[] { one[0], one[0] };

        _engine.Invoking(e => e.Generate(TournamentId, one, RaceTo5)).Should().Throw<DomainException>();
        _engine.Invoking(e => e.Generate(TournamentId, duplicate, RaceTo5)).Should().Throw<DomainException>();
        _engine.Invoking(e => e.Generate(TournamentId, [Guid.Empty, one[0]], RaceTo5)).Should().Throw<DomainException>();
    }
}
