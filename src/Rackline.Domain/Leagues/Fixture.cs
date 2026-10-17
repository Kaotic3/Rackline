using Rackline.Domain.Common;

namespace Rackline.Domain.Leagues;

public enum FixtureStatus
{
    Scheduled,
    Postponed,
    Completed,
}

/// <summary>One individual match within a fixture, as entered by the league secretary.</summary>
public sealed record RubberResult(Guid HomePlayerId, Guid AwayPlayerId, int HomeScore, int AwayScore);

/// <summary>A team match, made up of individual rubbers.</summary>
public sealed class Fixture : Entity
{
    private readonly List<Rubber> _rubbers = [];

    /// <summary>Used by EF Core when loading.</summary>
    private Fixture()
    {
    }

    internal Fixture(Guid seasonId, int round, Guid homeTeamId, Guid awayTeamId, DateTimeOffset? scheduledFor)
    {
        SeasonId = seasonId;
        Round = round;
        HomeTeamId = homeTeamId;
        AwayTeamId = awayTeamId;
        ScheduledFor = scheduledFor;
    }

    public Guid SeasonId { get; private set; }

    /// <summary>1-based round (usually a week) of the season's schedule.</summary>
    public int Round { get; private set; }

    public Guid HomeTeamId { get; private set; }

    public Guid AwayTeamId { get; private set; }

    /// <summary>When it is due to be played; null while postponed with no new date.</summary>
    public DateTimeOffset? ScheduledFor { get; private set; }

    public FixtureStatus Status { get; private set; } = FixtureStatus.Scheduled;

    public DateTimeOffset? CompletedAt { get; private set; }

    public IReadOnlyList<Rubber> Rubbers => _rubbers;

    public int HomeRubbersWon => _rubbers.Count(r => r.HomeScore > r.AwayScore);

    public int AwayRubbersWon => _rubbers.Count(r => r.AwayScore > r.HomeScore);

    public bool Involves(Guid teamId) => HomeTeamId == teamId || AwayTeamId == teamId;

    internal void SetResult(IEnumerable<Rubber> rubbers, DateTimeOffset at)
    {
        _rubbers.Clear();
        _rubbers.AddRange(rubbers);
        Status = FixtureStatus.Completed;
        CompletedAt = at;
    }

    internal void Reschedule(DateTimeOffset? scheduledFor)
    {
        Guard.That(Status != FixtureStatus.Completed, "A fixture that has been played cannot be rescheduled.");
        ScheduledFor = scheduledFor;
        Status = scheduledFor is null ? FixtureStatus.Postponed : FixtureStatus.Scheduled;
    }
}

public sealed class Rubber
{
    /// <summary>Used by EF Core when loading.</summary>
    private Rubber()
    {
    }

    internal Rubber(int number, Guid homePlayerId, Guid awayPlayerId, int homeScore, int awayScore)
    {
        Number = number;
        HomePlayerId = homePlayerId;
        AwayPlayerId = awayPlayerId;
        HomeScore = homeScore;
        AwayScore = awayScore;
    }

    public int Number { get; private set; }

    public Guid HomePlayerId { get; private set; }

    public Guid AwayPlayerId { get; private set; }

    public int HomeScore { get; private set; }

    public int AwayScore { get; private set; }
}
