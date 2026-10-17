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
    public void A_live_match_can_be_finished_through_the_bracket()
    {
        Opener.Start(At);

        _bracket.RecordResult(Opener.Number, 4, 5, At.AddHours(1));

        Opener.State.Should().Be(MatchState.Completed);
        Opener.CompletedAt.Should().Be(At.AddHours(1));
        Opener.WinnerEntryId.Should().Be(Opener.Slot2.EntryId);
        Opener.LoserEntryId.Should().Be(Opener.Slot1.EntryId);
    }

    [Fact]
    public void Frames_are_numbered_in_order_and_build_the_score()
    {
        _bracket.Invoking(b => b.RecordFrame(Opener.Number, 1, At)).Should().Throw<DomainException>("the match is not live");

        Opener.Start(At);
        _bracket.RecordFrame(Opener.Number, 1, At, breakAndRun: true);
        var second = _bracket.RecordFrame(Opener.Number, 2, At, points1: 34, points2: 71);
        _bracket.RecordFrame(Opener.Number, 2, At);

        Opener.Frames.Select(f => f.Number).Should().Equal(1, 2, 3);
        Opener.Frames[0].BreakAndRun.Should().BeTrue();
        second.Should().BeEquivalentTo(new { WinnerSlot = 2, Points1 = 34, Points2 = 71, BreakAndRun = false });
        (Opener.Score1, Opener.Score2).Should().Be((1, 2));
        Opener.State.Should().Be(MatchState.Live);
    }

    [Fact]
    public void Invalid_frames_change_nothing()
    {
        Opener.Start(At);

        _bracket.Invoking(b => b.RecordFrame(Opener.Number, 3, At)).Should().Throw<DomainException>();
        _bracket.Invoking(b => b.RecordFrame(Opener.Number, 1, At, points1: -5)).Should().Throw<DomainException>();

        Opener.Frames.Should().BeEmpty();
        (Opener.Score1, Opener.Score2).Should().Be((0, 0));
    }

    [Fact]
    public void The_deciding_frame_completes_the_match_and_moves_the_winner_on()
    {
        Opener.Start(At);
        for (var i = 0; i < 4; i++)
        {
            _bracket.RecordFrame(Opener.Number, 1, At);
            _bracket.RecordFrame(Opener.Number, 2, At);
        }

        Opener.State.Should().Be(MatchState.Live, "4–4 in a race to 5 is not over");

        _bracket.RecordFrame(Opener.Number, 2, At.AddHours(2));

        Opener.Should().BeEquivalentTo(new { State = MatchState.Completed, Score1 = 4, Score2 = 5, CompletedAt = At.AddHours(2) }, o => o.ExcludingMissingMembers());
        Opener.WinnerEntryId.Should().Be(Opener.Slot2.EntryId);
        Final.Slot1.EntryId.Should().Be(Opener.Slot2.EntryId);
        _bracket.Invoking(b => b.RecordFrame(Opener.Number, 1, At)).Should().Throw<DomainException>("the match is over");
    }

    [Fact]
    public void The_last_frame_can_be_taken_back_while_live()
    {
        _bracket.Invoking(b => b.UndoLastFrame(Opener.Number)).Should().Throw<DomainException>("the match is not live");

        Opener.Start(At);
        _bracket.Invoking(b => b.UndoLastFrame(Opener.Number)).Should().Throw<DomainException>("there are no frames yet");

        _bracket.RecordFrame(Opener.Number, 1, At);
        _bracket.RecordFrame(Opener.Number, 2, At, breakAndRun: true);
        _bracket.UndoLastFrame(Opener.Number);

        Opener.Frames.Should().ContainSingle().Which.WinnerSlot.Should().Be(1);
        (Opener.Score1, Opener.Score2).Should().Be((1, 0));

        _bracket.RecordFrame(Opener.Number, 1, At);
        Opener.Frames.Select(f => f.Number).Should().Equal(1, 2);
    }

    [Fact]
    public void A_match_scored_by_frame_cannot_take_a_whole_result()
    {
        Opener.Start(At);
        _bracket.RecordFrame(Opener.Number, 1, At);

        _bracket.Invoking(b => b.RecordResult(Opener.Number, 5, 0, At)).Should().Throw<DomainException>();
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
