using Rackline.Domain.Common;

namespace Rackline.Domain.Matches;

/// <summary>An optional per-frame record within a match.</summary>
public sealed class Frame
{
    /// <summary>Used by EF Core when loading.</summary>
    private Frame()
    {
    }

    internal Frame(int number, int winnerSlot, bool breakAndRun, int? points1, int? points2)
    {
        Guard.That(winnerSlot is 1 or 2, "Frame winner must be slot 1 or 2.");
        Guard.That(points1 is null or >= 0 && points2 is null or >= 0, "Frame points must not be negative.");
        Number = number;
        WinnerSlot = winnerSlot;
        BreakAndRun = breakAndRun;
        Points1 = points1;
        Points2 = points2;
    }

    public int Number { get; private set; }

    public int WinnerSlot { get; private set; }

    public bool BreakAndRun { get; private set; }

    /// <summary>Points scored by slot 1 (snooker), if recorded.</summary>
    public int? Points1 { get; private set; }

    /// <summary>Points scored by slot 2 (snooker), if recorded.</summary>
    public int? Points2 { get; private set; }
}
