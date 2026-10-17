using Rackline.Domain.Common;

namespace Rackline.Domain.Rankings;

/// <summary>A player's Elo rating in one discipline. Changed only by <see cref="Elo"/>.</summary>
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

    /// <summary>Too few rated matches for the rating to be settled; it moves faster until then.</summary>
    public bool IsProvisional => MatchesPlayed < Elo.ProvisionalMatches;

    internal void Apply(double newValue)
    {
        Guard.That(double.IsFinite(newValue), "Rating must be a finite number.");
        Value = newValue;
        MatchesPlayed++;
    }
}

/// <summary>How one rated match moved one player's rating.</summary>
public sealed class RatingChange : Entity
{
    /// <summary>Used by EF Core when loading.</summary>
    private RatingChange()
    {
    }

    internal RatingChange(Guid playerId, Guid opponentId, Discipline discipline, Guid matchId, bool won, double before, double after, DateTimeOffset at)
    {
        PlayerId = playerId;
        OpponentId = opponentId;
        Discipline = discipline;
        MatchId = matchId;
        Won = won;
        Before = before;
        After = after;
        At = at;
    }

    public Guid PlayerId { get; private set; }

    public Guid OpponentId { get; private set; }

    public Discipline Discipline { get; private set; }

    /// <summary>The tournament match that was rated.</summary>
    public Guid MatchId { get; private set; }

    public bool Won { get; private set; }

    public double Before { get; private set; }

    public double After { get; private set; }

    public DateTimeOffset At { get; private set; }
}
