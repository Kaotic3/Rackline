using Microsoft.EntityFrameworkCore;
using Rackline.Application.Common;
using Rackline.Application.Organisations;
using Rackline.Domain.Organisations;
using Rackline.Domain.Players;

namespace Rackline.Infrastructure.Persistence;

internal sealed class OrganisationDirectory(RacklineDbContext db) : IOrganisationDirectory
{
    public async Task<OrganisationLineage?> FindLineageAsync(Guid organisationId, CancellationToken cancellationToken)
    {
        // The hierarchy has at most four levels, so walk up one query at a time.
        var parents = new Dictionary<Guid, Guid?>();
        Guid? next = organisationId;
        while (next is { } id && !parents.ContainsKey(id))
        {
            var row = await db.Organisations.AsNoTracking()
                .Where(o => o.Id == id)
                .Select(o => new { o.ParentId })
                .SingleOrDefaultAsync(cancellationToken);
            if (row is null)
            {
                return id == organisationId ? null : throw new InvalidOperationException($"Organisation {id} is missing from the hierarchy.");
            }

            parents[id] = row.ParentId;
            next = row.ParentId;
        }

        return new OrganisationLineage(organisationId, parents);
    }

    public async Task<IReadOnlyList<Appointment>> AppointmentsForAsync(Guid userId, CancellationToken cancellationToken) =>
        await db.Appointments.AsNoTracking().Where(a => a.UserId == userId).ToListAsync(cancellationToken);
}

internal sealed class OrganisationRepository(RacklineDbContext db) : IOrganisationRepository
{
    public Task<Organisation?> FindAsync(Guid id, CancellationToken cancellationToken) =>
        db.Organisations.SingleOrDefaultAsync(o => o.Id == id, cancellationToken);

    public void Add(Organisation organisation) => db.Organisations.Add(organisation);

    public Task<Venue?> FindVenueAsync(Guid id, CancellationToken cancellationToken) =>
        db.Venues.Include(v => v.Tables).SingleOrDefaultAsync(v => v.Id == id, cancellationToken);

    public void Add(Venue venue) => db.Venues.Add(venue);

    public Task<Appointment?> FindAppointmentAsync(Guid organisationId, Guid userId, CancellationToken cancellationToken) =>
        db.Appointments.SingleOrDefaultAsync(a => a.OrganisationId == organisationId && a.UserId == userId, cancellationToken);

    public void Add(Appointment appointment) => db.Appointments.Add(appointment);

    public void Remove(Appointment appointment) => db.Appointments.Remove(appointment);

    public Task SaveChangesAsync(CancellationToken cancellationToken) => db.SaveWithConflictsAsync("organisation", cancellationToken);
}

internal sealed class PlayerRepository(RacklineDbContext db) : IPlayerRepository
{
    public Task<Player?> FindAsync(Guid id, CancellationToken cancellationToken) =>
        db.Players.SingleOrDefaultAsync(p => p.Id == id, cancellationToken);

    public void Add(Player player) => db.Players.Add(player);

    public Task SaveChangesAsync(CancellationToken cancellationToken) => db.SaveWithConflictsAsync("player", cancellationToken);
}

internal sealed class UserDirectory(RacklineDbContext db) : IUserDirectory
{
    public async Task<Guid?> FindIdByEmailAsync(string email, CancellationToken cancellationToken)
    {
        // Identity stores emails upper-cased for look-ups.
        var normalised = email.Trim().ToUpperInvariant();
        return await db.Users.AsNoTracking()
            .Where(u => u.NormalizedEmail == normalised)
            .Select(u => (Guid?)u.Id)
            .SingleOrDefaultAsync(cancellationToken);
    }
}
