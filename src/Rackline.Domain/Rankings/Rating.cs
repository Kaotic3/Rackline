using Rackline.Domain.Common;

namespace Rackline.Domain.Rankings;

/// <summary>A player's skill rating in one discipline.</summary>
public sealed class Rating : Entity
{
    public const double Initial = 1500;

    /// <summary>Used by EF Core when loading.</summary>
    private Rating()
    {
    }

    public Rating(Guid playerId, Discipline discipline)
    {
        PlayerId = Guard.NotEmpty(playerId, "Player");
        Discipline = discipline;
    }

    public Guid PlayerId { get; private set; }

    public Discipline Discipline { get; private set; }

    public double Value { get; private set; } = Initial;

    public int MatchesPlayed { get; private set; }

    public void Apply(double newValue)
    {
        Guard.That(double.IsFinite(newValue), "Rating must be a finite number.");
        Value = newValue;
        MatchesPlayed++;
    }
}
