using Microsoft.EntityFrameworkCore;
using Rackline.Application.Common;
using Rackline.Domain.Common;
using Rackline.Domain.Matches;
using Rackline.Domain.Rankings;
using Rackline.Domain.Tournaments;

namespace Rackline.Infrastructure.Persistence;

internal sealed class RankingRepository(RacklineDbContext db) : IRankingRepository
{
    public Task<Ranking?> FindAsync(Guid id, CancellationToken cancellationToken) =>
        db.Rankings.SingleOrDefaultAsync(r => r.Id == id, cancellationToken);

    public void Add(Ranking ranking) => db.Rankings.Add(ranking);

    public Task SaveChangesAsync(CancellationToken cancellationToken) => db.SaveWithConflictsAsync("ranking", cancellationToken);
}

internal sealed class RatingRepository(RacklineDbContext db) : IRatingRepository
{
    public Task<Rating?> FindAsync(Guid playerId, Discipline discipline, CancellationToken cancellationToken) =>
        db.Ratings.SingleOrDefaultAsync(r => r.PlayerId == playerId && r.Discipline == discipline, cancellationToken);

    public void Add(Rating rating) => db.Ratings.Add(rating);

    public void Add(RatingChange change) => db.RatingChanges.Add(change);

    public async Task<IReadOnlyList<RatedResult>> ListRatedResultsAsync(CancellationToken cancellationToken) =>
        await (
            from m in db.Matches.AsNoTracking()
            where m.State == MatchState.Completed && m.WinnerEntryId != null && m.LoserEntryId != null
            join t in db.Tournaments on m.TournamentId equals t.Id
            join winner in db.Set<Entry>() on m.WinnerEntryId equals (Guid?)winner.Id
            join loser in db.Set<Entry>() on m.LoserEntryId equals (Guid?)loser.Id
            orderby m.CompletedAt, m.TournamentId, m.Number
            select new RatedResult(m.Id, t.Discipline, winner.PlayerId, loser.PlayerId, m.CompletedAt!.Value))
            .ToListAsync(cancellationToken);

    public async Task ReplaceAllAsync(IReadOnlyList<Rating> ratings, IReadOnlyList<RatingChange> changes, CancellationToken cancellationToken)
    {
        await using var transaction = await db.Database.BeginTransactionAsync(cancellationToken);
        await db.RatingChanges.ExecuteDeleteAsync(cancellationToken);
        await db.Ratings.ExecuteDeleteAsync(cancellationToken);
        db.Ratings.AddRange(ratings);
        db.RatingChanges.AddRange(changes);
        await db.SaveWithConflictsAsync("rating", cancellationToken);
        await transaction.CommitAsync(cancellationToken);
    }
}
