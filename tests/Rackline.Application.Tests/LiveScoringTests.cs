using FluentAssertions;
using Rackline.Application.Common;
using Rackline.Application.Tournaments;
using Rackline.Domain.Common;
using Rackline.Domain.Matches;
using Rackline.Domain.Tournaments;

namespace Rackline.Application.Tests;

public class LiveScoringTests
{
    private static readonly DateTimeOffset Now = new(2026, 8, 1, 20, 0, 0, TimeSpan.Zero);

    private readonly InMemoryTournaments _tournaments = new();
    private readonly RecordingNotifier _notifier = new();
    private readonly FixedTime _time = new(Now);
    private readonly LiveScoring _scoring;
    private readonly Tournament _tournament;

    public LiveScoringTests()
    {
        _scoring = new LiveScoring(_tournaments, new InMemoryRatings(), Access.Admin(), _notifier, _time);

        _tournament = new Tournament(Guid.CreateVersion7(), "Friday Frames", Discipline.EnglishEightBall, TournamentFormat.SingleElimination, MatchLength.RaceTo(2));
        _tournament.OpenRegistration();
        for (var i = 0; i < 4; i++)
        {
            _tournament.AddEntry(Guid.CreateVersion7());
        }

        _tournament.MakeDraw(new SeededRandom(3));
        _tournament.Start();
        _tournament.StartMatch(1, Now);
        _tournaments.Add(_tournament);
        _tournaments.SaveChangesAsync(default).GetAwaiter().GetResult();
    }

    private Match Opener => _tournament.Matches.Single(m => m.Number == 1);

    [Fact]
    public async Task A_frame_is_saved_and_only_its_match_is_announced()
    {
        await _scoring.RecordFrameAsync(_tournament.Id, 1, winnerSlot: 2, breakAndRun: true, points1: null, points2: null);

        Opener.Should().BeEquivalentTo(new { Score1 = 0, Score2 = 1, State = MatchState.Live }, o => o.ExcludingMissingMembers());
        var announced = _notifier.MatchBatches.Should().ContainSingle().Subject;
        announced.Should().ContainSingle().Which.Should().BeEquivalentTo(new { Number = 1, Score2 = 1, BreakAndRuns2 = 1 }, o => o.ExcludingMissingMembers());
    }

    [Fact]
    public async Task The_deciding_frame_announces_the_finished_match_and_the_next_one()
    {
        await _scoring.RecordFrameAsync(_tournament.Id, 1, 1, false, null, null);
        _time.Now = Now.AddMinutes(20);
        await _scoring.RecordFrameAsync(_tournament.Id, 1, 1, false, null, null);

        Opener.Should().BeEquivalentTo(new { State = MatchState.Completed, CompletedAt = Now.AddMinutes(20) }, o => o.ExcludingMissingMembers());
        _notifier.MatchBatches[^1].Select(m => m.Number).Should().Equal(1, 3);
        _notifier.MatchBatches[^1].Single(m => m.Number == 3).Slot1.EntryId.Should().Be(Opener.WinnerEntryId);
    }

    [Fact]
    public async Task Undoing_a_frame_is_saved_and_announced()
    {
        await _scoring.RecordFrameAsync(_tournament.Id, 1, 2, false, null, null);
        await _scoring.UndoLastFrameAsync(_tournament.Id, 1);

        (Opener.Score1, Opener.Score2).Should().Be((0, 0));
        _notifier.MatchBatches.Should().HaveCount(2);
        _notifier.MatchBatches[1].Single().Score2.Should().Be(0);
    }

    [Fact]
    public async Task Unknown_tournaments_and_matches_are_not_found()
    {
        (await FluentActions.Awaiting(() => _scoring.RecordFrameAsync(Guid.CreateVersion7(), 1, 1, false, null, null))
            .Should().ThrowAsync<NotFoundException>()).Which.Resource.Should().Be("Tournament");
        (await FluentActions.Awaiting(() => _scoring.UndoLastFrameAsync(_tournament.Id, 42))
            .Should().ThrowAsync<NotFoundException>()).Which.Resource.Should().Be("Match");
        _notifier.MatchBatches.Should().BeEmpty();
    }

    [Fact]
    public async Task Frames_for_a_match_that_is_not_live_break_a_rule()
    {
        await FluentActions.Awaiting(() => _scoring.RecordFrameAsync(_tournament.Id, 2, 1, false, null, null))
            .Should().ThrowAsync<DomainException>();
        _notifier.MatchBatches.Should().BeEmpty();
    }
}
