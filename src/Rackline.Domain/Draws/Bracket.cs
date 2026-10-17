using Rackline.Domain.Common;
using Rackline.Domain.Matches;

namespace Rackline.Domain.Draws;

/// <summary>
/// The set of matches in a draw, and the rules for moving entries between them as results come in.
/// </summary>
public sealed class Bracket
{
    private readonly Dictionary<int, Match> _matches;

    public Bracket(IEnumerable<Match> matches)
    {
        _matches = matches.ToDictionary(m => m.Number);
    }

    public IReadOnlyCollection<Match> Matches => _matches.Values;

    public Match this[int number] =>
        _matches.TryGetValue(number, out var match) ? match : throw new DomainException($"There is no match {number}.");

    public bool IsFinished => _matches.Values.All(m => m.IsResolved);

    /// <summary>
    /// Winner of an elimination draw once it is finished; null for unfinished or round-robin draws.
    /// </summary>
    public Guid? ChampionEntryId
    {
        get
        {
            if (!IsFinished)
            {
                return null;
            }

            var final = _matches.Values.SingleOrDefault(m =>
                m.Side != BracketSide.Group && m.WinnerTo is null && m.ResetMatchNumber is null);

            if (final is null)
            {
                return null;
            }

            if (final.IsVoid)
            {
                // Grand-final reset was not needed: the first grand final decided it.
                return _matches.Values.Single(m => m.ResetMatchNumber == final.Number).WinnerEntryId;
            }

            return final.WinnerEntryId;
        }
    }

    /// <summary>Puts an entry or a bye into a slot and cascades any bye resolutions.</summary>
    public void Place(int matchNumber, int slot, MatchSlot value)
    {
        var match = this[matchNumber];
        match.Place(slot, value);
        if (match.IsResolved)
        {
            Advance(match);
        }
    }

    /// <summary>Records a whole result at once. Not allowed once a match is being scored frame by frame.</summary>
    public void RecordResult(int matchNumber, int score1, int score2, DateTimeOffset at)
    {
        var match = this[matchNumber];
        Guard.That(match.Frames.Count == 0, $"Match {matchNumber} is being scored frame by frame; record the remaining frames instead.");
        match.Complete(score1, score2, at);
        Advance(match);
    }

    /// <summary>
    /// Records the next frame of a live match. The frame that takes a player to the target
    /// completes the match and moves the players on.
    /// </summary>
    public Frame RecordFrame(int matchNumber, int winnerSlot, DateTimeOffset at, bool breakAndRun = false, int? points1 = null, int? points2 = null)
    {
        var match = this[matchNumber];
        var frame = match.AddFrame(winnerSlot, breakAndRun, points1, points2);

        if (match.HasReachedTarget)
        {
            match.Complete(match.Score1, match.Score2, at);
            Advance(match);
        }

        return frame;
    }

    /// <summary>Takes back the last frame of a live match.</summary>
    public void UndoLastFrame(int matchNumber) => this[matchNumber].RemoveLastFrame();

    public void AwardWalkover(int matchNumber, Guid winnerEntryId, DateTimeOffset at)
    {
        var match = this[matchNumber];
        match.AwardWalkover(winnerEntryId, at);
        Advance(match);
    }

    private void Advance(Match resolved)
    {
        var queue = new Queue<Match>([resolved]);

        while (queue.TryDequeue(out var match))
        {
            if (match.ResetMatchNumber is { } resetNumber)
            {
                var reset = this[resetNumber];
                var slot2Won = match.WinnerEntryId is not null && match.WinnerEntryId == match.Slot2.EntryId;

                Fill(reset, 1, slot2Won ? match.Slot1 : MatchSlot.Bye, queue);
                Fill(reset, 2, slot2Won ? match.Slot2 : MatchSlot.Bye, queue);
            }

            if (match.WinnerTo is { } winnerTo)
            {
                Fill(this[winnerTo.MatchNumber], winnerTo.Slot, MatchSlot.ForOrBye(match.WinnerEntryId), queue);
            }

            if (match.LoserTo is { } loserTo)
            {
                Fill(this[loserTo.MatchNumber], loserTo.Slot, MatchSlot.ForOrBye(match.LoserEntryId), queue);
            }
        }
    }

    private static void Fill(Match target, int slot, MatchSlot value, Queue<Match> queue)
    {
        target.Place(slot, value);
        if (target.IsResolved)
        {
            queue.Enqueue(target);
        }
    }
}
