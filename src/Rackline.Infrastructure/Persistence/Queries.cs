using Microsoft.EntityFrameworkCore;
using Rackline.Application.Players;
using Rackline.Application.Tournaments;
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
                        m.LoserTo == null ? null : new SlotLinkDto(m.LoserTo.MatchNumber, m.LoserTo.Slot)))
                    .ToList()))
            .AsSplitQuery()
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
}
