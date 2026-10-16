namespace Rackline.Domain.Common;

public enum MatchLengthKind
{
    RaceTo,
    BestOf,
}

/// <summary>How long a match is: a race to N frames, or best of N (N odd).</summary>
public sealed record MatchLength
{
    private MatchLength(MatchLengthKind kind, int value)
    {
        Kind = kind;
        Value = value;
    }

    public MatchLengthKind Kind { get; }

    public int Value { get; }

    /// <summary>Frames a player needs to win the match.</summary>
    public int FramesToWin => Kind == MatchLengthKind.RaceTo ? Value : (Value + 1) / 2;

    public static MatchLength RaceTo(int frames) =>
        new(MatchLengthKind.RaceTo, Guard.Positive(frames, "Race-to"));

    public static MatchLength BestOf(int frames)
    {
        Guard.Positive(frames, "Best-of");
        Guard.That(frames % 2 == 1, "Best-of must be an odd number of frames.");
        return new MatchLength(MatchLengthKind.BestOf, frames);
    }

    public override string ToString() =>
        Kind == MatchLengthKind.RaceTo ? $"Race to {Value}" : $"Best of {Value}";
}
