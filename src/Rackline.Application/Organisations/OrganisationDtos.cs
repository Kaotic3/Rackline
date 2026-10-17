using Rackline.Domain.Common;
using Rackline.Domain.Organisations;
using Rackline.Domain.Tournaments;

namespace Rackline.Application.Organisations;

public sealed record OrganisationSummaryDto(Guid Id, string Name, OrganisationLevel Level, Guid? ParentId);

public sealed record VenueDto(Guid Id, string Name, string? Address, IReadOnlyList<VenueTableDto> Tables);

public sealed record VenueTableDto(Guid Id, string Label);

public sealed record OrganisationTournamentDto(Guid Id, string Name, Discipline Discipline, TournamentStatus Status);

public sealed record OrganisationLinkDto(Guid Id, string Name);

public sealed record OrganisationDetailDto(
    Guid Id,
    string Name,
    OrganisationLevel Level,
    OrganisationSummaryDto? Parent,
    IReadOnlyList<OrganisationSummaryDto> Children,
    IReadOnlyList<VenueDto> Venues,
    IReadOnlyList<OrganisationTournamentDto> Tournaments,
    IReadOnlyList<OrganisationLinkDto> Leagues,
    IReadOnlyList<OrganisationLinkDto> Rankings,
    int MemberCount);

/// <summary>A member of staff appointed at an organisation.</summary>
public sealed record StaffDto(Guid UserId, string? Email, StaffRole Role);

/// <summary>One of the signed-in user's own appointments.</summary>
public sealed record MyAppointmentDto(Guid OrganisationId, string OrganisationName, OrganisationLevel Level, StaffRole Role);

public interface IOrganisationQueries
{
    /// <summary>Every organisation, for building the tree.</summary>
    Task<IReadOnlyList<OrganisationSummaryDto>> ListAsync(CancellationToken cancellationToken);

    /// <summary>
    /// The organisation with its venues, child organisations, leagues, rankings and public
    /// tournaments (drafts are left out).
    /// </summary>
    Task<OrganisationDetailDto?> GetAsync(Guid organisationId, CancellationToken cancellationToken);

    /// <summary>Staff appointed at this organisation itself, not those above it.</summary>
    Task<IReadOnlyList<StaffDto>> StaffAsync(Guid organisationId, CancellationToken cancellationToken);

    Task<IReadOnlyList<MyAppointmentDto>> AppointmentsOfAsync(Guid userId, CancellationToken cancellationToken);
}
