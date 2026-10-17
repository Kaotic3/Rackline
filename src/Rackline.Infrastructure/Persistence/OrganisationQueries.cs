using Microsoft.EntityFrameworkCore;
using Rackline.Application.Organisations;
using Rackline.Domain.Tournaments;

namespace Rackline.Infrastructure.Persistence;

internal sealed class OrganisationQueries(RacklineDbContext db) : IOrganisationQueries
{
    // Levels are stored as text, so the database would sort them alphabetically; sort in memory instead.
    public async Task<IReadOnlyList<OrganisationSummaryDto>> ListAsync(CancellationToken cancellationToken) =>
        [
            .. (await db.Organisations
                .AsNoTracking()
                .Select(o => new OrganisationSummaryDto(o.Id, o.Name, o.Level, o.ParentId))
                .ToListAsync(cancellationToken))
                .OrderBy(o => o.Level).ThenBy(o => o.Name, StringComparer.CurrentCultureIgnoreCase),
        ];

    public async Task<OrganisationDetailDto?> GetAsync(Guid organisationId, CancellationToken cancellationToken)
    {
        var organisation = await db.Organisations
            .AsNoTracking()
            .Where(o => o.Id == organisationId)
            .Select(o => new
            {
                o.Id,
                o.Name,
                o.Level,
                Parent = db.Organisations
                    .Where(p => p.Id == o.ParentId)
                    .Select(p => new OrganisationSummaryDto(p.Id, p.Name, p.Level, p.ParentId))
                    .FirstOrDefault(),
                Children = db.Organisations
                    .Where(c => c.ParentId == o.Id)
                    .OrderBy(c => c.Name)
                    .Select(c => new OrganisationSummaryDto(c.Id, c.Name, c.Level, c.ParentId))
                    .ToList(),
                Venues = db.Venues
                    .Where(v => v.OrganisationId == o.Id)
                    .OrderBy(v => v.Name)
                    .Select(v => new VenueDto(v.Id, v.Name, v.Address, v.Tables.OrderBy(t => t.Label).Select(t => new VenueTableDto(t.Id, t.Label)).ToList()))
                    .ToList(),
                Tournaments = db.Tournaments
                    .Where(t => t.OrganisationId == o.Id && t.Status != TournamentStatus.Draft)
                    .OrderBy(t => t.Name)
                    .Select(t => new OrganisationTournamentDto(t.Id, t.Name, t.Discipline, t.Status))
                    .ToList(),
                Leagues = db.Leagues
                    .Where(l => l.OrganisationId == o.Id)
                    .OrderBy(l => l.Name)
                    .Select(l => new OrganisationLinkDto(l.Id, l.Name))
                    .ToList(),
                Rankings = db.Rankings
                    .Where(r => r.OrganisationId == o.Id)
                    .OrderByDescending(r => r.Season).ThenBy(r => r.Name)
                    .Select(r => new OrganisationLinkDto(r.Id, r.Name))
                    .ToList(),
                MemberCount = db.Players.Count(p => p.OrganisationIds.Contains(o.Id)),
            })
            .AsSplitQuery()
            .SingleOrDefaultAsync(cancellationToken);

        return organisation is null
            ? null
            : new OrganisationDetailDto(
                organisation.Id,
                organisation.Name,
                organisation.Level,
                organisation.Parent,
                organisation.Children,
                organisation.Venues,
                organisation.Tournaments,
                organisation.Leagues,
                organisation.Rankings,
                organisation.MemberCount);
    }

    public async Task<IReadOnlyList<StaffDto>> StaffAsync(Guid organisationId, CancellationToken cancellationToken) =>
        await db.Appointments
            .AsNoTracking()
            .Where(a => a.OrganisationId == organisationId)
            .Join(db.Users, a => a.UserId, u => u.Id, (a, u) => new { a.UserId, u.Email, a.Role })
            .OrderByDescending(x => x.Role).ThenBy(x => x.Email)
            .Select(x => new StaffDto(x.UserId, x.Email, x.Role))
            .ToListAsync(cancellationToken);

    public async Task<IReadOnlyList<MyAppointmentDto>> AppointmentsOfAsync(Guid userId, CancellationToken cancellationToken) =>
        [
            .. (await db.Appointments
                .AsNoTracking()
                .Where(a => a.UserId == userId)
                .Join(db.Organisations, a => a.OrganisationId, o => o.Id, (a, o) => new MyAppointmentDto(o.Id, o.Name, o.Level, a.Role))
                .ToListAsync(cancellationToken))
                .OrderBy(x => x.Level).ThenBy(x => x.OrganisationName, StringComparer.CurrentCultureIgnoreCase),
        ];
}
