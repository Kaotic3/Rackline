namespace Rackline.Domain.Matches;

public enum MatchSlotKind
{
    /// <summary>Not yet known; waiting on an earlier match.</summary>
    Awaiting,

    /// <summary>Holds an entry.</summary>
    Entry,

    /// <summary>Will never hold an entry.</summary>
    Bye,
}

/// <summary>One side of a match: an entry, a bye, or not yet decided.</summary>
public sealed record MatchSlot
{
    private MatchSlot(MatchSlotKind kind, Guid? entryId)
    {
        Kind = kind;
        EntryId = entryId;
    }

    public static MatchSlot Awaiting { get; } = new(MatchSlotKind.Awaiting, null);

    public static MatchSlot Bye { get; } = new(MatchSlotKind.Bye, null);

    public MatchSlotKind Kind { get; }

    public Guid? EntryId { get; }

    public bool IsAwaiting => Kind == MatchSlotKind.Awaiting;

    public bool IsBye => Kind == MatchSlotKind.Bye;

    public bool HasEntry => Kind == MatchSlotKind.Entry;

    public static MatchSlot For(Guid entryId) => new(MatchSlotKind.Entry, entryId);

    /// <summary>An entry slot when given an id, otherwise a bye.</summary>
    public static MatchSlot ForOrBye(Guid? entryId) => entryId is { } id ? For(id) : Bye;
}

/// <summary>Points at a slot (1 or 2) of another match in the same tournament.</summary>
public sealed record SlotLink(int MatchNumber, int Slot);
