using Microsoft.EntityFrameworkCore;
using Rackline.Domain.Organisations;
using Rackline.Application.Players;
using Rackline.Application.Tournaments;
using Rackline.Application.Venues;
using Rackline.Domain.Matches;
using Rackline.Domain.Tournaments;

namespace Rackline.Infrastructure.Persistence;

internal sealed class TournamentQueries(RacklineDbContext db) : ITournamentQueries
{
    public async Task<IReadOnlyList<TournamentSummaryDto>> ListAsync(TournamentStatus? status, CancellationToken cancellationToken) =>
        await db.Tournaments
            .AsNoTracking()
            .Where(t => status == null || t.Status == status)
            .OrderBy(t => t.Name)
            .Select(t => new TournamentSummaryDto(t.Id, t.OrganisationId, t.Name, t.Discipline, t.Format, t.Status, t.Entries.Count))
            .ToListAsync(cancellationToken);

    public Task<TournamentDetailDto?> GetAsync(Guid id, CancellationToken cancellationToken) =>
        db.Tournaments
            .AsNoTracking()
            .Where(t => t.Id == id)
            .Select(t => new TournamentDetailDto(
                t.Id,
                t.OrganisationId,
                t.Name,
                t.Discipline,
                t.Format,
                t.Status,
                new MatchLengthDto(t.MatchLength.Kind, t.MatchLength.Value),
                t.Options.GrandFinalReset,
                t.Options.GroupCount,
                t.Entries
                    .OrderBy(e => e.Seed == null)
                    .ThenBy(e => e.Seed)
                    .Join(db.Players, e => e.PlayerId, p => p.Id, (e, p) =>
                        new EntryDto(e.Id, e.PlayerId, p.FirstName + " " + p.LastName, e.Seed, e.Handicap))
                    .ToList(),
                t.Matches
                    .OrderBy(m => m.Number)
                    .Select(m => new MatchDto(
                        m.Id,
                        m.Number,
                        m.Side,
                        m.Round,
                        m.Position,
                        m.Group,
                        new SlotDto(m.Slot1.Kind, m.Slot1.EntryId),
                        new SlotDto(m.Slot2.Kind, m.Slot2.EntryId),
                        m.Score1,
                        m.Score2,
                        m.State,
                        m.WinnerEntryId,
                        m.TableId,
                        m.StartedAt,
                        m.CompletedAt,
                        m.WinnerTo == null ? null : new SlotLinkDto(m.WinnerTo.MatchNumber, m.WinnerTo.Slot),
                        m.LoserTo == null ? null : new SlotLinkDto(m.LoserTo.MatchNumber, m.LoserTo.Slot),
                        m.Frames.Count(f => f.BreakAndRun && f.WinnerSlot == 1),
                        m.Frames.Count(f => f.BreakAndRun && f.WinnerSlot == 2)))
                    .ToList(),
                t.RankingId,
                db.Rankings.Where(r => r.Id == t.RankingId).Select(r => r.Name).FirstOrDefault()))
            .AsSplitQuery()
            .SingleOrDefaultAsync(cancellationToken);

    public async Task<Guid?> FindTournamentIdForMatchAsync(Guid matchId, CancellationToken cancellationToken) =>
        await db.Matches
            .AsNoTracking()
            .Where(m => m.Id == matchId)
            .Select(m => (Guid?)m.TournamentId)
            .SingleOrDefaultAsync(cancellationToken);
}

internal sealed class PlayerQueries(RacklineDbContext db) : IPlayerQueries
{
    public async Task<IReadOnlyList<PlayerSummaryDto>> ListAsync(string? search, CancellationToken cancellationToken)
    {
        var players = db.Players.AsNoTracking();
        if (!string.IsNullOrWhiteSpace(search))
        {
            var pattern = $"%{search.Trim()}%";
            players = players.Where(p => EF.Functions.ILike(p.FirstName + " " + p.LastName, pattern));
        }

        return await players
            .OrderBy(p => p.LastName).ThenBy(p => p.FirstName)
            .Select(p => new PlayerSummaryDto(p.Id, p.FirstName, p.LastName, p.Nationality, p.LicenceNumber))
            .ToListAsync(cancellationToken);
    }

    public async Task<PlayerProfileDto?> GetProfileAsync(Guid playerId, CancellationToken cancellationToken)
    {
        var player = await db.Players
            .AsNoTracking()
            .Where(p => p.Id == playerId)
            .Select(p => new { p.Id, p.FirstName, p.LastName, p.Nationality, p.LicenceNumber, p.OrganisationIds })
            .SingleOrDefaultAsync(cancellationToken);

        if (player is null)
        {
            return null;
        }

        var organisationIds = player.OrganisationIds.ToList();
        var organisations = await db.Organisations
            .AsNoTracking()
            .Where(o => organisationIds.Contains(o.Id))
            .OrderBy(o => o.Level).ThenBy(o => o.Name)
            .Select(o => o.Name)
            .ToListAsync(cancellationToken);

        var ratings = await db.Ratings
            .AsNoTracking()
            .Where(r => r.PlayerId == playerId)
            .OrderBy(r => r.Discipline)
            .Select(r => new RatingDto(r.Discipline, r.Value, r.MatchesPlayed))
            .ToListAsync(cancellationToken);

        return new PlayerProfileDto(player.Id, player.FirstName, player.LastName, player.Nationality, player.LicenceNumber, organisations, ratings);
    }

    public async Task<IReadOnlyList<PlayedMatchDto>> GetPlayedMatchesAsync(Guid playerId, CancellationToken cancellationToken)
    {
        // 1. The player's entries, with their tournaments.
        var entries = await db.Tournaments
            .AsNoTracking()
            .SelectMany(t => t.Entries.Where(e => e.PlayerId == playerId), (t, e) => new { EntryId = e.Id, TournamentId = t.Id, t.Name, t.Discipline })
            .ToListAsync(cancellationToken);
        if (entries.Count == 0)
        {
            return [];
        }

        var mine = entries.Select(e => (Guid?)e.EntryId).ToList();

        // 2. Their finished matches against a real opponent (no byes or void matches).
        var matches = await db.Matches
            .AsNoTracking()
            .Where(m => (m.State == MatchState.Completed || m.State == MatchState.Walkover)
                && m.Slot1.Kind == MatchSlotKind.Entry && m.Slot2.Kind == MatchSlotKind.Entry
                && (mine.Contains(m.Slot1.EntryId) || mine.Contains(m.Slot2.EntryId)))
            .Select(m => new
            {
                m.Id,
                m.TournamentId,
                Entry1 = m.Slot1.EntryId!.Value,
                Entry2 = m.Slot2.EntryId!.Value,
                m.Score1,
                m.Score2,
                m.State,
                m.WinnerEntryId,
                m.CompletedAt,
                BreakAndRuns1 = m.Frames.Count(f => f.BreakAndRun && f.WinnerSlot == 1),
                BreakAndRuns2 = m.Frames.Count(f => f.BreakAndRun && f.WinnerSlot == 2),
            })
            .ToListAsync(cancellationToken);

        // 3. One query for every opponent's name.
        var mineSet = entries.Select(e => e.EntryId).ToHashSet();
        var opponentEntryIds = matches.Select(m => mineSet.Contains(m.Entry1) ? m.Entry2 : m.Entry1).Distinct().ToList();
        var opponents = await db.Set<Entry>()
            .AsNoTracking()
            .Where(e => opponentEntryIds.Contains(e.Id))
            .Join(db.Players, e => e.PlayerId, p => p.Id, (e, p) => new { EntryId = e.Id, PlayerId = p.Id, Name = p.FirstName + " " + p.LastName })
            .ToDictionaryAsync(x => x.EntryId, cancellationToken);

        var tournaments = entries.ToDictionary(e => e.TournamentId);
        return
        [
            .. matches
                .Select(m =>
                {
                    var isSlot1 = mineSet.Contains(m.Entry1);
                    var opponent = opponents[isSlot1 ? m.Entry2 : m.Entry1];
                    var walkover = m.State == MatchState.Walkover;
                    var tournament = tournaments[m.TournamentId];
                    return new PlayedMatchDto(
                        m.Id,
                        m.TournamentId,
                        tournament.Name,
                        tournament.Discipline,
                        m.CompletedAt,
                        opponent.PlayerId,
                        opponent.Name,
                        Won: m.WinnerEntryId == (isSlot1 ? m.Entry1 : m.Entry2),
                        walkover,
                        FramesFor: walkover ? 0 : isSlot1 ? m.Score1 : m.Score2,
                        FramesAgainst: walkover ? 0 : isSlot1 ? m.Score2 : m.Score1,
                        BreakAndRunsFor: isSlot1 ? m.BreakAndRuns1 : m.BreakAndRuns2,
                        BreakAndRunsAgainst: isSlot1 ? m.BreakAndRuns2 : m.BreakAndRuns1);
                })
                .OrderByDescending(m => m.PlayedAt),
        ];
    }
}

internal sealed class VenueQueries(RacklineDbContext db) : IVenueQueries
{
    public async Task<IReadOnlyList<TableDto>> ListTablesAsync(CancellationToken cancellationToken) =>
        await db.Set<Table>()
            .AsNoTracking()
            .Join(db.Venues, t => t.VenueId, v => v.Id, (t, v) => new { Table = t, Venue = v })
            .OrderBy(x => x.Venue.Name).ThenBy(x => x.Table.Label)
            .Select(x => new TableDto(x.Table.Id, x.Table.Label, x.Venue.Id, x.Venue.Name))
            .ToListAsync(cancellationToken);
}
