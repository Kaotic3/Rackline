using Rackline.Domain.Common;

namespace Rackline.Domain.Matches;

/// <summary>
/// A single match in a tournament. Results that affect other matches go through
/// <see cref="Draws.Bracket"/>, which handles progression.
/// </summary>
public sealed class Match : Entity
{
    private readonly List<Frame> _frames = [];

    /// <summary>Used by EF Core when loading.</summary>
    private Match()
    {
    }

    internal Match(Guid tournamentId, int number, BracketSide side, int round, int position, int framesToWin, int? group = null)
    {
        TournamentId = tournamentId;
        Number = Guard.Positive(number, "Match number");
        Side = side;
        Round = Guard.Positive(round, "Round");
        Position = Guard.Positive(position, "Position");
        FramesToWin = Guard.Positive(framesToWin, "Frames to win");
        Group = group;
    }

    public Guid TournamentId { get; private set; }

    /// <summary>Unique within the tournament; used to link matches together.</summary>
    public int Number { get; private set; }

    public BracketSide Side { get; private set; }

    public int Round { get; private set; }

    /// <summary>1-based position of the match within its round.</summary>
    public int Position { get; private set; }

    /// <summary>Group number for round-robin matches.</summary>
    public int? Group { get; private set; }

    public int FramesToWin { get; private set; }

    public MatchSlot Slot1 { get; private set; } = MatchSlot.Awaiting;

    public MatchSlot Slot2 { get; private set; } = MatchSlot.Awaiting;

    public int Score1 { get; private set; }

    public int Score2 { get; private set; }

    public MatchState State { get; private set; } = MatchState.Pending;

    public Guid? WinnerEntryId { get; private set; }

    public Guid? LoserEntryId { get; private set; }

    public Guid? TableId { get; private set; }

    public DateTimeOffset? StartedAt { get; private set; }

    public DateTimeOffset? CompletedAt { get; private set; }

    /// <summary>Where the winner goes next, if anywhere.</summary>
    public SlotLink? WinnerTo { get; private set; }

    /// <summary>Where the loser goes next (double elimination), if anywhere.</summary>
    public SlotLink? LoserTo { get; private set; }

    /// <summary>
    /// For a grand final with a reset: the match played only if the slot 2 player wins this one.
    /// </summary>
    public int? ResetMatchNumber { get; private set; }

    public IReadOnlyList<Frame> Frames => _frames;

    public bool IsResolved => State is MatchState.Completed or MatchState.Walkover;

    /// <summary>Resolved automatically because one slot was a bye.</summary>
    public bool IsBye => State == MatchState.Walkover && (Slot1.IsBye ^ Slot2.IsBye);

    /// <summary>Resolved with no winner because neither slot will ever hold an entry.</summary>
    public bool IsVoid => State == MatchState.Walkover && Slot1.IsBye && Slot2.IsBye;

    public MatchSlot GetSlot(int slot) => slot switch
    {
        1 => Slot1,
        2 => Slot2,
        _ => throw new DomainException("Slot must be 1 or 2."),
    };

    public bool Involves(Guid entryId) => Slot1.EntryId == entryId || Slot2.EntryId == entryId;

    public void AssignTable(Guid? tableId)
    {
        Guard.That(!IsResolved, "Cannot assign a table to a finished match.");
        TableId = tableId;
    }

    public void Start(DateTimeOffset at, Guid? tableId = null)
    {
        Guard.That(State == MatchState.Ready, $"Match {Number} is not ready to start.");
        State = MatchState.Live;
        StartedAt = at;
        TableId = tableId ?? TableId;
    }

    /// <summary>
    /// Records the next frame of a live match and adds it to the winner's score. Use the bracket,
    /// which completes the match when the deciding frame is recorded.
    /// </summary>
    internal Frame AddFrame(int winnerSlot, bool breakAndRun, int? points1, int? points2)
    {
        Guard.That(State == MatchState.Live, $"Match {Number} is not live.");
        var frame = new Frame(_frames.Count + 1, winnerSlot, breakAndRun, points1, points2);
        _frames.Add(frame);

        if (winnerSlot == 1)
        {
            Score1++;
        }
        else
        {
            Score2++;
        }

        return frame;
    }

    /// <summary>Takes back the most recent frame of a live match, e.g. after a mis-tap.</summary>
    internal void RemoveLastFrame()
    {
        Guard.That(State == MatchState.Live, "Only a live match can have a frame taken back.");
        Guard.That(_frames.Count > 0, $"Match {Number} has no frames to take back.");

        var last = _frames[^1];
        _frames.RemoveAt(_frames.Count - 1);
        if (last.WinnerSlot == 1)
        {
            Score1--;
        }
        else
        {
            Score2--;
        }
    }

    internal bool HasReachedTarget => Math.Max(Score1, Score2) >= FramesToWin;

    internal void LinkWinnerTo(int matchNumber, int slot) => WinnerTo = new SlotLink(matchNumber, slot);

    internal void LinkLoserTo(int matchNumber, int slot) => LoserTo = new SlotLink(matchNumber, slot);

    internal void LinkReset(int matchNumber) => ResetMatchNumber = matchNumber;

    /// <summary>Fills a slot, then resolves the match automatically if it involves a bye.</summary>
    internal void Place(int slot, MatchSlot value)
    {
        Guard.That(!value.IsAwaiting, "Cannot place an empty value into a slot.");
        Guard.That(GetSlot(slot).IsAwaiting, $"Slot {slot} of match {Number} is already filled.");

        if (slot == 1)
        {
            Slot1 = value;
        }
        else
        {
            Slot2 = value;
        }

        if (Slot1.IsAwaiting || Slot2.IsAwaiting)
        {
            return;
        }

        if (Slot1.HasEntry && Slot2.HasEntry)
        {
            State = MatchState.Ready;
            return;
        }

        State = MatchState.Walkover;
        WinnerEntryId = Slot1.EntryId ?? Slot2.EntryId;
    }

    internal void Complete(int score1, int score2, DateTimeOffset at)
    {
        Guard.That(State is MatchState.Ready or MatchState.Live, $"Match {Number} cannot be completed from {State}.");
        Guard.That(score1 >= 0 && score2 >= 0, "Scores must not be negative.");
        Guard.That(
            Math.Max(score1, score2) == FramesToWin && Math.Min(score1, score2) < FramesToWin,
            $"Exactly one player must reach {FramesToWin}.");

        Score1 = score1;
        Score2 = score2;
        State = MatchState.Completed;
        CompletedAt = at;
        (WinnerEntryId, LoserEntryId) = score1 > score2
            ? (Slot1.EntryId, Slot2.EntryId)
            : (Slot2.EntryId, Slot1.EntryId);
    }

    internal void AwardWalkover(Guid winnerEntryId, DateTimeOffset at)
    {
        Guard.That(State is MatchState.Ready or MatchState.Live, $"Match {Number} cannot be awarded from {State}.");
        Guard.That(Involves(winnerEntryId), $"Entry is not playing in match {Number}.");

        State = MatchState.Walkover;
        CompletedAt = at;
        WinnerEntryId = winnerEntryId;
        LoserEntryId = Slot1.EntryId == winnerEntryId ? Slot2.EntryId : Slot1.EntryId;
    }
}
