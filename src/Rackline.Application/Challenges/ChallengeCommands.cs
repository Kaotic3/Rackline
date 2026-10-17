using Rackline.Application.Common;
using Rackline.Application.Organisations;
using Rackline.Application.Rankings;
using Rackline.Domain.Challenges;
using Rackline.Domain.Common;
using Rackline.Domain.Organisations;
using Rackline.Domain.Players;
using Rackline.Domain.Rankings;

namespace Rackline.Application.Challenges;

public interface IChallengeRepository
{
    Task<Challenge?> FindAsync(Guid id, CancellationToken cancellationToken);

    /// <summary>When these two players have met in rated challenges in the discipline, either way round.</summary>
    Task<IReadOnlyList<DateTimeOffset>> RatedMeetingsAsync(Guid player1Id, Guid player2Id, Discipline discipline, CancellationToken cancellationToken);

    void Add(Challenge challenge);

    void Remove(Challenge challenge);

    /// <summary>Saves all changes, including any rating changes. Throws <see cref="ConflictException"/> if someone else saved first.</summary>
    Task SaveChangesAsync(CancellationToken cancellationToken);
}

/// <summary>
/// Recording challenges. A referee or organiser records the result after the match, for two players
/// who both belong to their organisation (or one below it), so results are witnessed rather than
/// self-reported. Whether it counts towards ratings is decided then, by <see cref="ChallengeRules"/>.
/// </summary>
public sealed class ChallengeRecording(
    IChallengeRepository challenges,
    IPlayerRepository players,
    IRatingRepository ratings,
    OrganisationAccess access,
    RatingRebuild rebuild,
    TimeProvider time)
{
    /// <summary>Whether a challenge between the two players would count if it were recorded now.</summary>
    public async Task<ChallengeAssessment> PreviewAsync(Guid player1Id, Guid player2Id, Discipline discipline, CancellationToken cancellationToken = default)
    {
        await GetPlayerAsync(player1Id, cancellationToken);
        await GetPlayerAsync(player2Id, cancellationToken);
        return await AssessAsync(player1Id, player2Id, discipline, time.GetUtcNow(), cancellationToken);
    }

    public async Task<Guid> RecordAsync(
        Guid organisationId,
        Guid player1Id,
        Guid player2Id,
        Discipline discipline,
        MatchLengthKind lengthKind,
        int lengthValue,
        int score1,
        int score2,
        CancellationToken cancellationToken = default)
    {
        await access.EnsureAsync(organisationId, StaffRole.Referee, cancellationToken);
        foreach (var playerId in new[] { player1Id, player2Id }.Distinct())
        {
            var player = await GetPlayerAsync(playerId, cancellationToken);
            if (!await BelongsAsync(player, organisationId, cancellationToken))
            {
                throw new DomainException($"{player.DisplayName} is not a member of this organisation, or of one below it.");
            }
        }

        var at = time.GetUtcNow();
        var length = lengthKind == MatchLengthKind.RaceTo ? MatchLength.RaceTo(lengthValue) : MatchLength.BestOf(lengthValue);
        var assessment = await AssessAsync(player1Id, player2Id, discipline, at, cancellationToken);
        var challenge = new Challenge(organisationId, player1Id, player2Id, discipline, length, score1, score2, at, assessment);
        challenges.Add(challenge);

        if (challenge.ToRatedResult() is { } result)
        {
            // Both players have settled ratings, or the challenge would not count.
            var winner = await ratings.FindAsync(result.WinnerPlayerId, discipline, cancellationToken) ?? throw new InvalidOperationException("Rating missing.");
            var loser = await ratings.FindAsync(result.LoserPlayerId, discipline, cancellationToken) ?? throw new InvalidOperationException("Rating missing.");
            var (won, lost) = Elo.Rate(winner, loser, result.MatchId, result.At);
            ratings.Add(won);
            ratings.Add(lost);
        }

        await challenges.SaveChangesAsync(cancellationToken);
        return challenge.Id;
    }

    /// <summary>
    /// Removes a challenge recorded in error. If it counted, every rating is worked out again
    /// without it, as later results were rated on top of it.
    /// </summary>
    public async Task DeleteAsync(Guid challengeId, CancellationToken cancellationToken = default)
    {
        var challenge = await challenges.FindAsync(challengeId, cancellationToken) ?? throw new NotFoundException("Challenge", challengeId);
        await access.EnsureAsync(challenge.OrganisationId, StaffRole.Organiser, cancellationToken);

        challenges.Remove(challenge);
        await challenges.SaveChangesAsync(cancellationToken);
        if (challenge.Rated)
        {
            await rebuild.RebuildAsync(cancellationToken);
        }
    }

    private async Task<ChallengeAssessment> AssessAsync(Guid player1Id, Guid player2Id, Discipline discipline, DateTimeOffset at, CancellationToken cancellationToken) =>
        ChallengeRules.Assess(
            await ratings.FindAsync(player1Id, discipline, cancellationToken),
            await ratings.FindAsync(player2Id, discipline, cancellationToken),
            at,
            await challenges.RatedMeetingsAsync(player1Id, player2Id, discipline, cancellationToken));

    private async Task<Player> GetPlayerAsync(Guid playerId, CancellationToken cancellationToken) =>
        await players.FindAsync(playerId, cancellationToken) ?? throw new NotFoundException("Player", playerId);

    /// <summary>A member of the organisation itself, or of any organisation below it.</summary>
    private async Task<bool> BelongsAsync(Player player, Guid organisationId, CancellationToken cancellationToken)
    {
        foreach (var membership in player.OrganisationIds)
        {
            if ((await access.LineageAsync(membership, cancellationToken)).Includes(organisationId))
            {
                return true;
            }
        }

        return false;
    }
}
