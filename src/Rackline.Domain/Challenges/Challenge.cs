using Rackline.Domain.Common;

namespace Rackline.Domain.Challenges;

public enum ChallengeStatus
{
    Proposed,
    Accepted,
    Declined,
    Completed,
}

/// <summary>An ad hoc match between two players, outside any tournament.</summary>
public sealed class Challenge : Entity
{
    /// <summary>Used by EF Core when loading.</summary>
    private Challenge()
    {
        MatchLength = null!;
    }

    public Challenge(Guid challengerId, Guid opponentId, Discipline discipline, MatchLength matchLength)
    {
        Guard.That(challengerId != opponentId, "A player cannot challenge themselves.");
        ChallengerId = Guard.NotEmpty(challengerId, "Challenger");
        OpponentId = Guard.NotEmpty(opponentId, "Opponent");
        Discipline = discipline;
        MatchLength = matchLength;
    }

    public Guid ChallengerId { get; private set; }

    public Guid OpponentId { get; private set; }

    public Discipline Discipline { get; private set; }

    public MatchLength MatchLength { get; private set; }

    public ChallengeStatus Status { get; private set; } = ChallengeStatus.Proposed;

    public int ChallengerScore { get; private set; }

    public int OpponentScore { get; private set; }

    public DateTimeOffset? CompletedAt { get; private set; }

    public Guid? WinnerId => Status != ChallengeStatus.Completed ? null
        : ChallengerScore > OpponentScore ? ChallengerId : OpponentId;

    public void Accept()
    {
        Guard.That(Status == ChallengeStatus.Proposed, "Only a proposed challenge can be accepted.");
        Status = ChallengeStatus.Accepted;
    }

    public void Decline()
    {
        Guard.That(Status == ChallengeStatus.Proposed, "Only a proposed challenge can be declined.");
        Status = ChallengeStatus.Declined;
    }

    public void RecordResult(int challengerScore, int opponentScore, DateTimeOffset at)
    {
        Guard.That(Status == ChallengeStatus.Accepted, "Only an accepted challenge can be completed.");
        var target = MatchLength.FramesToWin;
        Guard.That(
            challengerScore >= 0 && opponentScore >= 0
            && Math.Max(challengerScore, opponentScore) == target
            && Math.Min(challengerScore, opponentScore) < target,
            $"Exactly one player must reach {target}.");

        ChallengerScore = challengerScore;
        OpponentScore = opponentScore;
        CompletedAt = at;
        Status = ChallengeStatus.Completed;
    }
}
