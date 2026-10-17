using Microsoft.EntityFrameworkCore;
using Npgsql;
using Rackline.Application.Common;
using Rackline.Domain.Tournaments;

namespace Rackline.Infrastructure.Persistence;

internal sealed class TournamentRepository(RacklineDbContext db) : ITournamentRepository
{
    public Task<Tournament?> FindAsync(Guid id, CancellationToken cancellationToken) =>
        db.Tournaments
            .Include(t => t.Entries)
            .Include(t => t.Matches)
            .AsSplitQuery()
            .SingleOrDefaultAsync(t => t.Id == id, cancellationToken);

    public void Add(Tournament tournament) => db.Tournaments.Add(tournament);

    public void Remove(Tournament tournament) => db.Tournaments.Remove(tournament);

    public async Task SaveChangesAsync(CancellationToken cancellationToken)
    {
        try
        {
            await db.SaveChangesAsync(cancellationToken);
        }
        catch (DbUpdateConcurrencyException ex)
        {
            throw new ConflictException("Someone else changed this tournament at the same time. Reload it and try again.", ex);
        }
        catch (DbUpdateException ex) when (ex.InnerException is PostgresException { SqlState: PostgresErrorCodes.UniqueViolation })
        {
            throw new ConflictException("That change clashes with another one made at the same time. Reload and try again.", ex);
        }
    }
}

internal sealed class ReferenceData(RacklineDbContext db) : IReferenceData
{
    public Task<bool> OrganisationExistsAsync(Guid id, CancellationToken cancellationToken) =>
        db.Organisations.AnyAsync(o => o.Id == id, cancellationToken);

    public Task<bool> PlayerExistsAsync(Guid id, CancellationToken cancellationToken) =>
        db.Players.AnyAsync(p => p.Id == id, cancellationToken);

    public Task<bool> TableExistsAsync(Guid id, CancellationToken cancellationToken) =>
        db.Venues.SelectMany(v => v.Tables).AnyAsync(t => t.Id == id, cancellationToken);
}
