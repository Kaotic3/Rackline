using Microsoft.EntityFrameworkCore;
using Rackline.Application.Challenges;
using Rackline.Application.Tournaments;
using Rackline.Domain.Challenges;
using Rackline.Domain.Common;

namespace Rackline.Infrastructure.Persistence;

internal sealed class ChallengeRepository(RacklineDbContext db) : IChallengeRepository
{
    public Task<Challenge?> FindAsync(Guid id, CancellationToken cancellationToken) =>
        db.Challenges.SingleOrDefaultAsync(c => c.Id == id, cancellationToken);

    public async Task<IReadOnlyList<DateTimeOffset>> RatedMeetingsAsync(Guid player1Id, Guid player2Id, Discipline discipline, CancellationToken cancellationToken) =>
        await db.Challenges.AsNoTracking()
            .Where(c => c.Rated && c.Discipline == discipline
                && ((c.Player1Id == player1Id && c.Player2Id == player2Id) || (c.Player1Id == player2Id && c.Player2Id == player1Id)))
            .Select(c => c.PlayedAt)
            .ToListAsync(cancellationToken);

    public void Add(Challenge challenge) => db.Challenges.Add(challenge);

    public void Remove(Challenge challenge) => db.Challenges.Remove(challenge);

    public Task SaveChangesAsync(CancellationToken cancellationToken) => db.SaveWithConflictsAsync("challenge", cancellationToken);
}

internal sealed class ChallengeQueries(RacklineDbContext db) : IChallengeQueries
{
    public async Task<IReadOnlyList<ChallengeDto>> ListAsync(Guid? playerId, int limit, CancellationToken cancellationToken)
    {
        var challenges = db.Challenges.AsNoTracking();
        if (playerId is { } id)
        {
            challenges = challenges.Where(c => c.Player1Id == id || c.Player2Id == id);
        }

        return await Project(challenges.OrderByDescending(c => c.PlayedAt).Take(limit)).ToListAsync(cancellationToken);
    }

    public Task<ChallengeDto?> GetAsync(Guid challengeId, CancellationToken cancellationToken) =>
        Project(db.Challenges.AsNoTracking().Where(c => c.Id == challengeId)).SingleOrDefaultAsync(cancellationToken);

    /// <summary>Order and filter before calling this: EF cannot translate either once rows are DTOs.</summary>
    private IQueryable<ChallengeDto> Project(IQueryable<Challenge> challenges) =>
        from c in challenges
        join o in db.Organisations on c.OrganisationId equals o.Id
        join p1 in db.Players on c.Player1Id equals p1.Id
        join p2 in db.Players on c.Player2Id equals p2.Id
        select new ChallengeDto(
            c.Id,
            c.OrganisationId,
            o.Name,
            c.Discipline,
            new MatchLengthDto(c.MatchLength.Kind, c.MatchLength.Value),
            c.Player1Id,
            p1.FirstName + " " + p1.LastName,
            c.Player2Id,
            p2.FirstName + " " + p2.LastName,
            c.Score1,
            c.Score2,
            c.PlayedAt,
            c.Rated,
            c.UnratedReason,
            db.RatingChanges.Where(r => r.MatchId == c.Id && r.PlayerId == c.Player1Id).Select(r => (double?)(r.After - r.Before)).FirstOrDefault(),
            db.RatingChanges.Where(r => r.MatchId == c.Id && r.PlayerId == c.Player2Id).Select(r => (double?)(r.After - r.Before)).FirstOrDefault());
}
