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

    public void RecordResult(int matchNumber, int score1, int score2, DateTimeOffset at)
    {
        var match = this[matchNumber];
        match.Complete(score1, score2, at);
        Advance(match);
    }

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
