using Rackline.Application.Tournaments;
using Rackline.Domain.Common;

namespace Rackline.Application.Challenges;

public sealed record ChallengeDto(
    Guid Id,
    Guid OrganisationId,
    string OrganisationName,
    Discipline Discipline,
    MatchLengthDto MatchLength,
    Guid Player1Id,
    string Player1Name,
    Guid Player2Id,
    string Player2Name,
    int Score1,
    int Score2,
    DateTimeOffset PlayedAt,
    bool Rated,
    string? UnratedReason,
    double? RatingChange1,
    double? RatingChange2)
{
    public Guid WinnerId => Score1 > Score2 ? Player1Id : Player2Id;
}

/// <summary>Whether a challenge would count towards ratings if recorded now, and why not.</summary>
public sealed record ChallengePreviewDto(bool Counts, IReadOnlyList<string> Reasons);

public interface IChallengeQueries
{
    /// <summary>Challenges, newest first; only the player's when <paramref name="playerId"/> is given.</summary>
    Task<IReadOnlyList<ChallengeDto>> ListAsync(Guid? playerId, int limit, CancellationToken cancellationToken);

    Task<ChallengeDto?> GetAsync(Guid challengeId, CancellationToken cancellationToken);
}
