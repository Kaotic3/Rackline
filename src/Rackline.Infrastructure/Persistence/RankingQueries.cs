using Microsoft.EntityFrameworkCore;
using Rackline.Application.Rankings;
using Rackline.Domain.Common;
using Rackline.Domain.Rankings;
using Rackline.Domain.Tournaments;

namespace Rackline.Infrastructure.Persistence;

internal sealed class RankingQueries(RacklineDbContext db) : IRankingQueries
{
    public async Task<IReadOnlyList<RankingSummaryDto>> ListAsync(CancellationToken cancellationToken) =>
        await db.Rankings
            .AsNoTracking()
            .Join(db.Organisations, r => r.OrganisationId, o => o.Id, (r, o) => new { Ranking = r, Organisation = o.Name })
            .OrderByDescending(x => x.Ranking.Season).ThenBy(x => x.Ranking.Name)
            .Select(x => new RankingSummaryDto(
                x.Ranking.Id,
                x.Ranking.OrganisationId,
                x.Organisation,
                x.Ranking.Name,
                x.Ranking.Discipline,
                x.Ranking.Season,
                x.Ranking.Awards.Select(a => a.TournamentId).Distinct().Count()))
            .ToListAsync(cancellationToken);

    public async Task<RankingDetailDto?> GetAsync(Guid rankingId, CancellationToken cancellationToken)
    {
        var ranking = await db.Rankings
            .AsNoTracking()
            .Where(r => r.Id == rankingId)
            .Join(db.Organisations, r => r.OrganisationId, o => o.Id, (r, o) => new
            {
                r.Id,
                r.OrganisationId,
                Organisation = o.Name,
                r.Name,
                r.Discipline,
                r.Season,
                r.PointsByPosition,
                r.ParticipationPoints,
                Awards = r.Awards.Select(a => new { a.TournamentId, a.PlayerId, a.Points }).ToList(),
            })
            .AsSplitQuery()
            .SingleOrDefaultAsync(cancellationToken);

        if (ranking is null)
        {
            return null;
        }

        var playerIds = ranking.Awards.Select(a => a.PlayerId).Distinct().ToList();
        var names = await PlayerNamesAsync(playerIds, cancellationToken);

        var counted = ranking.Awards.Select(a => a.TournamentId).ToHashSet();
        var tournaments = await db.Tournaments
            .AsNoTracking()
            .Where(t => t.RankingId == rankingId || counted.Contains(t.Id))
            .OrderBy(t => t.Name)
            .Select(t => new { t.Id, t.Name, t.Status })
            .ToListAsync(cancellationToken);

        var table = RankingTable.Calculate(ranking.Awards.Select(a => (a.PlayerId, a.Points)))
            .Select(r => new RankingRowDto(r.Position, r.PlayerId, names[r.PlayerId], r.Points, r.Tournaments))
            .ToList();

        return new RankingDetailDto(
            ranking.Id,
            ranking.OrganisationId,
            ranking.Organisation,
            ranking.Name,
            ranking.Discipline,
            ranking.Season,
            [.. ranking.PointsByPosition],
            ranking.ParticipationPoints,
            table,
            [.. tournaments.Select(t => new RankedTournamentDto(t.Id, t.Name, t.Status, counted.Contains(t.Id)))]);
    }

    public async Task<IReadOnlyList<PlayerRankingDto>> ForPlayerAsync(Guid playerId, CancellationToken cancellationToken)
    {
        // The player's position depends on everyone else's points, so load each of their rankings' awards.
        var rankings = await db.Rankings
            .AsNoTracking()
            .Where(r => r.Awards.Any(a => a.PlayerId == playerId))
            .OrderByDescending(r => r.Season).ThenBy(r => r.Name)
            .Select(r => new
            {
                r.Id,
                r.Name,
                r.Discipline,
                r.Season,
                Awards = r.Awards.Select(a => new { a.PlayerId, a.Points }).ToList(),
            })
            .AsSplitQuery()
            .ToListAsync(cancellationToken);

        return
        [
            .. rankings.Select(r =>
            {
                var row = RankingTable.Calculate(r.Awards.Select(a => (a.PlayerId, a.Points))).Single(x => x.PlayerId == playerId);
                return new PlayerRankingDto(r.Id, r.Name, r.Discipline, r.Season, row.Position, row.Points);
            }),
        ];
    }

    private Task<Dictionary<Guid, string>> PlayerNamesAsync(List<Guid> playerIds, CancellationToken cancellationToken) =>
        db.Players
            .AsNoTracking()
            .Where(p => playerIds.Contains(p.Id))
            .ToDictionaryAsync(p => p.Id, p => p.FirstName + " " + p.LastName, cancellationToken);
}

internal sealed class RatingQueries(RacklineDbContext db) : IRatingQueries
{
    public async Task<IReadOnlyList<Discipline>> RatedDisciplinesAsync(CancellationToken cancellationToken) =>
        await db.Ratings.AsNoTracking().Select(r => r.Discipline).Distinct().OrderBy(d => d).ToListAsync(cancellationToken);

    public async Task<IReadOnlyList<RatingRowDto>> LeaderboardAsync(Discipline discipline, CancellationToken cancellationToken)
    {
        var rows = await db.Ratings
            .AsNoTracking()
            .Where(r => r.Discipline == discipline)
            .Join(db.Players, r => r.PlayerId, p => p.Id, (r, p) => new { r.Value, r.MatchesPlayed, r.PlayerId, p.FirstName, p.LastName, p.Nationality })
            .OrderByDescending(x => x.Value).ThenBy(x => x.LastName).ThenBy(x => x.FirstName).ThenBy(x => x.PlayerId)
            .ToListAsync(cancellationToken);

        // Players on exactly the same rating share a position.
        return [.. rows.Select((x, i) => new RatingRowDto(
            rows.FindIndex(o => o.Value == x.Value) + 1, x.PlayerId, x.FirstName + " " + x.LastName, x.Nationality, x.Value, x.MatchesPlayed))];
    }

    public async Task<IReadOnlyList<RatingChangeDto>> HistoryAsync(Guid playerId, int limit, CancellationToken cancellationToken) =>
        await (
            from c in db.RatingChanges.AsNoTracking()
            where c.PlayerId == playerId
            join opponent in db.Players on c.OpponentId equals opponent.Id
            join m in db.Matches on c.MatchId equals m.Id into matches
            from m in matches.DefaultIfEmpty()
            join t in db.Tournaments on m.TournamentId equals t.Id into tournaments
            from t in tournaments.DefaultIfEmpty()
            orderby c.At descending, c.MatchId descending
            select new RatingChangeDto(
                c.Discipline,
                c.MatchId,
                t == null ? null : t.Id,
                t == null ? null : t.Name,
                c.OpponentId,
                opponent.FirstName + " " + opponent.LastName,
                c.Won,
                c.Before,
                c.After,
                c.At))
            .Take(limit)
            .ToListAsync(cancellationToken);
}
