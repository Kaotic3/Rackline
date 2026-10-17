using Rackline.Domain.Common;

namespace Rackline.Domain.Leagues;

/// <summary>How a season's team matches are played and scored.</summary>
public sealed record SeasonRules
{
    /// <summary>Used by EF Core when loading.</summary>
    private SeasonRules()
    {
        RubberLength = null!;
    }

    /// <param name="rubbersPerFixture">Individual matches in each team match.</param>
    /// <param name="rubberLength">Length of each individual match.</param>
    /// <param name="pointsForWin">Table points for winning a fixture.</param>
    /// <param name="pointsForDraw">Table points for drawing a fixture.</param>
    /// <param name="pointsPerRubber">Extra table points for every rubber won (0 for none).</param>
    public SeasonRules(int rubbersPerFixture, MatchLength rubberLength, int pointsForWin = 2, int pointsForDraw = 1, int pointsPerRubber = 0)
    {
        Guard.That(rubbersPerFixture is >= 1 and <= 30, "A fixture must have between 1 and 30 rubbers.");
        Guard.NotNegative(pointsForDraw, "Points for a draw");
        Guard.NotNegative(pointsPerRubber, "Points per rubber");
        Guard.That(pointsForWin >= pointsForDraw, "A win must be worth at least as much as a draw.");

        RubbersPerFixture = rubbersPerFixture;
        RubberLength = rubberLength;
        PointsForWin = pointsForWin;
        PointsForDraw = pointsForDraw;
        PointsPerRubber = pointsPerRubber;
    }

    public int RubbersPerFixture { get; private set; }

    public MatchLength RubberLength { get; private set; }

    public int PointsForWin { get; private set; }

    public int PointsForDraw { get; private set; }

    public int PointsPerRubber { get; private set; }
}
