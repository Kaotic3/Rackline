using FluentAssertions;
using Rackline.Domain.Common;
using Rackline.Domain.Draws;
using Rackline.Domain.Matches;
using static Rackline.Domain.Tests.TestSupport;

namespace Rackline.Domain.Tests.Matches;

public class MatchTests
{
    private readonly Bracket _bracket = new(new SingleEliminationEngine().Generate(TournamentId, Entries(4), RaceTo5));

    private Match Opener => _bracket[1];

    private Match Final => _bracket.Matches.Single(m => m.Round == 2);

    [Fact]
    public void Starting_a_ready_match_makes_it_live()
    {
        var table = Guid.CreateVersion7();

        Opener.Start(At, table);

        Opener.State.Should().Be(MatchState.Live);
        Opener.StartedAt.Should().Be(At);
        Opener.TableId.Should().Be(table);
    }

    [Fact]
    public void Only_ready_matches_can_start()
    {
        Final.Invoking(m => m.Start(At)).Should().Throw<DomainException>();

        Opener.Start(At);
        Opener.Invoking(m => m.Start(At)).Should().Throw<DomainException>();
    }

    [Fact]
    public void Live_scores_stay_below_the_target()
    {
        Opener.Invoking(m => m.UpdateScore(1, 0)).Should().Throw<DomainException>("the match is not live");

        Opener.Start(At);
        Opener.UpdateScore(4, 3);
        Opener.Score1.Should().Be(4);
        Opener.Score2.Should().Be(3);

        Opener.Invoking(m => m.UpdateScore(5, 3)).Should().Throw<DomainException>();
        Opener.Invoking(m => m.UpdateScore(-1, 3)).Should().Throw<DomainException>();
    }

    [Fact]
    public void A_live_match_can_be_finished_through_the_bracket()
    {
        Opener.Start(At);
        Opener.UpdateScore(4, 4);

        _bracket.RecordResult(Opener.Number, 4, 5, At.AddHours(1));

        Opener.State.Should().Be(MatchState.Completed);
        Opener.CompletedAt.Should().Be(At.AddHours(1));
        Opener.WinnerEntryId.Should().Be(Opener.Slot2.EntryId);
        Opener.LoserEntryId.Should().Be(Opener.Slot1.EntryId);
    }

    [Fact]
    public void Frames_are_numbered_in_order_while_live()
    {
        Opener.Invoking(m => m.RecordFrame(1)).Should().Throw<DomainException>();

        Opener.Start(At);
        Opener.RecordFrame(1, breakAndRun: true);
        var second = Opener.RecordFrame(2, points1: 34, points2: 71);

        Opener.Frames.Select(f => f.Number).Should().Equal(1, 2);
        Opener.Frames[0].BreakAndRun.Should().BeTrue();
        second.Should().BeEquivalentTo(new { WinnerSlot = 2, Points1 = 34, Points2 = 71, BreakAndRun = false });
        Opener.Invoking(m => m.RecordFrame(3)).Should().Throw<DomainException>();
        Opener.Invoking(m => m.RecordFrame(1, points1: -5)).Should().Throw<DomainException>();
    }

    [Fact]
    public void Tables_can_be_assigned_until_the_match_is_finished()
    {
        var table = Guid.CreateVersion7();
        Final.AssignTable(table);
        Final.TableId.Should().Be(table);

        _bracket.RecordResult(Opener.Number, 5, 0, At);
        Opener.Invoking(m => m.AssignTable(table)).Should().Throw<DomainException>();
    }

    [Fact]
    public void Slots_are_read_by_number()
    {
        Opener.GetSlot(1).Should().Be(Opener.Slot1);
        Opener.GetSlot(2).Should().Be(Opener.Slot2);
        Opener.Invoking(m => m.GetSlot(3)).Should().Throw<DomainException>();
    }

    [Fact]
    public void A_filled_slot_cannot_be_filled_again()
    {
        _bracket.Invoking(b => b.Place(Opener.Number, 1, MatchSlot.Bye)).Should().Throw<DomainException>();
        _bracket.Invoking(b => b.Place(Final.Number, 1, MatchSlot.Awaiting)).Should().Throw<DomainException>();
    }

    [Fact]
    public void Match_slots_describe_entries_and_byes()
    {
        var id = Guid.CreateVersion7();

        MatchSlot.For(id).Should().BeEquivalentTo(new { HasEntry = true, EntryId = id, IsBye = false });
        MatchSlot.ForOrBye(null).Should().Be(MatchSlot.Bye);
        MatchSlot.ForOrBye(id).Should().Be(MatchSlot.For(id));
        MatchSlot.Awaiting.IsAwaiting.Should().BeTrue();
    }
}

public class MatchLengthTests
{
    [Fact]
    public void Race_to_needs_that_many_frames()
    {
        var length = MatchLength.RaceTo(9);

        length.FramesToWin.Should().Be(9);
        length.ToString().Should().Be("Race to 9");
    }

    [Fact]
    public void Best_of_needs_a_majority()
    {
        var length = MatchLength.BestOf(7);

        length.FramesToWin.Should().Be(4);
        length.ToString().Should().Be("Best of 7");
    }

    [Theory]
    [InlineData(0)]
    [InlineData(-3)]
    public void Lengths_must_be_positive(int frames)
    {
        FluentActions.Invoking(() => MatchLength.RaceTo(frames)).Should().Throw<DomainException>();
        FluentActions.Invoking(() => MatchLength.BestOf(frames)).Should().Throw<DomainException>();
    }

    [Fact]
    public void Best_of_must_be_odd() =>
        FluentActions.Invoking(() => MatchLength.BestOf(8)).Should().Throw<DomainException>();
}
