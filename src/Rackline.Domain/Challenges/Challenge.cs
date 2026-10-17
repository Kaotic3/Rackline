using Rackline.Domain.Common;
using Rackline.Domain.Rankings;

namespace Rackline.Domain.Challenges;

/// <summary>
/// A one-off match between two players outside any tournament, recorded by a referee or organiser
/// once it has been played. Whether it counts towards ratings is decided once, when it is recorded,
/// by <see cref="ChallengeRules"/>, and stored.
/// </summary>
public sealed class Challenge : Entity
{
    /// <summary>Used by EF Core when loading.</summary>
    private Challenge()
    {
        MatchLength = null!;
    }

    public Challenge(
        Guid organisationId,
        Guid player1Id,
        Guid player2Id,
        Discipline discipline,
        MatchLength matchLength,
        int score1,
        int score2,
        DateTimeOffset playedAt,
        ChallengeAssessment assessment)
    {
        OrganisationId = Guard.NotEmpty(organisationId, "Organisation");
        Player1Id = Guard.NotEmpty(player1Id, "Player");
        Player2Id = Guard.NotEmpty(player2Id, "Opponent");
        Guard.That(player1Id != player2Id, "A player cannot play a challenge against themselves.");

        var target = matchLength.FramesToWin;
        Guard.That(
            score1 >= 0 && score2 >= 0 && Math.Max(score1, score2) == target && Math.Min(score1, score2) < target,
            $"Exactly one player must reach {target}.");

        Discipline = discipline;
        MatchLength = matchLength;
        Score1 = score1;
        Score2 = score2;
        PlayedAt = playedAt;
        Rated = assessment.Counts;
        UnratedReason = assessment.Counts ? null : string.Join(" ", assessment.Reasons);
    }

    /// <summary>The organisation whose referee or organiser recorded the result.</summary>
    public Guid OrganisationId { get; private set; }

    public Guid Player1Id { get; private set; }

    public Guid Player2Id { get; private set; }

    public Discipline Discipline { get; private set; }

    public MatchLength MatchLength { get; private set; }

    public int Score1 { get; private set; }

    public int Score2 { get; private set; }

    public DateTimeOffset PlayedAt { get; private set; }

    /// <summary>Whether the result counts towards ratings, decided when it was recorded.</summary>
    public bool Rated { get; private set; }

    /// <summary>Why the result does not count towards ratings, when it does not.</summary>
    public string? UnratedReason { get; private set; }

    public Guid WinnerId => Score1 > Score2 ? Player1Id : Player2Id;

    public Guid LoserId => Score1 > Score2 ? Player2Id : Player1Id;

    public bool Involves(Guid playerId) => Player1Id == playerId || Player2Id == playerId;

    /// <summary>The result as it counts towards ratings; null when it does not count.</summary>
    public RatedResult? ToRatedResult() => Rated ? new RatedResult(Id, Discipline, WinnerId, LoserId, PlayedAt) : null;
}

/// <summary>Whether a challenge would count towards ratings, and if not, why not.</summary>
public sealed record ChallengeAssessment(bool Counts, IReadOnlyList<string> Reasons)
{
    public static ChallengeAssessment Rated { get; } = new(true, []);
}

/// <summary>
/// Stops ratings being farmed through challenges. A challenge counts only between two players with
/// settled ratings that are close together, and only once per pair in a while. Tournament matches
/// are not limited like this: the organiser, not the players, decides who meets whom.
/// </summary>
public static class ChallengeRules
{
    /// <summary>
    /// The largest rating gap that still counts. At 200 points the favourite is expected to win about
    /// three times in four, so a win still says something about both players.
    /// </summary>
    public const double MaxRatingGap = 200;

    /// <summary>Only one rated challenge per pair of players, per discipline, in this long.</summary>
    public static readonly TimeSpan RepeatWindow = TimeSpan.FromDays(30);

    /// <param name="rating1">The first player's rating in the discipline; null if they have none yet.</param>
    /// <param name="rating2">The second player's rating in the discipline; null if they have none yet.</param>
    /// <param name="playedAt">When the challenge was played.</param>
    /// <param name="ratedMeetings">When the same two players last met in rated challenges in this discipline.</param>
    public static ChallengeAssessment Assess(Rating? rating1, Rating? rating2, DateTimeOffset playedAt, IEnumerable<DateTimeOffset> ratedMeetings)
    {
        var reasons = new List<string>();

        if (rating1 is not { IsProvisional: false } || rating2 is not { IsProvisional: false })
        {
            reasons.Add($"Both players need a settled rating (at least {Elo.ProvisionalMatches} rated matches) for a challenge to count.");
        }
        else if (Math.Abs(rating1.Value - rating2.Value) is var gap && gap > MaxRatingGap)
        {
            reasons.Add($"The players' ratings are {gap:0} points apart; a challenge counts only within {MaxRatingGap:0}.");
        }

        if (ratedMeetings.Any(at => (playedAt - at).Duration() < RepeatWindow))
        {
            reasons.Add($"These players already have a rated challenge within {RepeatWindow.Days} days of this one.");
        }

        return reasons.Count == 0 ? ChallengeAssessment.Rated : new ChallengeAssessment(false, reasons);
    }
}
