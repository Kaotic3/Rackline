using Rackline.Domain.Organisations;

namespace Rackline.Application.Common;

/// <summary>Who is making the request.</summary>
public interface ICurrentUser
{
    /// <summary>Null when nobody is signed in.</summary>
    Guid? UserId { get; }

    /// <summary>Site administrators may do anything, anywhere.</summary>
    bool IsAdmin { get; }
}

/// <summary>The organisation hierarchy and staff appointments, as needed for permission checks.</summary>
public interface IOrganisationDirectory
{
    /// <summary>The organisation and everything above it; null if there is no such organisation.</summary>
    Task<OrganisationLineage?> FindLineageAsync(Guid organisationId, CancellationToken cancellationToken);

    Task<IReadOnlyList<Appointment>> AppointmentsForAsync(Guid userId, CancellationToken cancellationToken);
}

/// <summary>
/// Checks what the current user may do at an organisation. Admins may do anything. Everyone else
/// needs an appointment at the organisation, or one above it, with a high enough role.
/// </summary>
public sealed class OrganisationAccess(ICurrentUser user, IOrganisationDirectory directory)
{
    private IReadOnlyList<Appointment>? _appointments;

    public bool IsAdmin => user.IsAdmin;

    /// <summary>The organisation and everything above it. Throws <see cref="NotFoundException"/> for an unknown organisation.</summary>
    public async Task<OrganisationLineage> LineageAsync(Guid organisationId, CancellationToken cancellationToken) =>
        await directory.FindLineageAsync(organisationId, cancellationToken) ?? throw new NotFoundException("Organisation", organisationId);

    public async Task<bool> CanAsync(Guid organisationId, StaffRole needed, CancellationToken cancellationToken)
    {
        if (user.IsAdmin)
        {
            return true;
        }

        if (user.UserId is not { } userId)
        {
            return false;
        }

        var lineage = await LineageAsync(organisationId, cancellationToken);
        _appointments ??= await directory.AppointmentsForAsync(userId, cancellationToken);
        return StaffPermissions.Allows(_appointments, lineage, needed);
    }

    /// <summary>Throws <see cref="ForbiddenException"/> unless the user has at least <paramref name="needed"/> at the organisation.</summary>
    public async Task EnsureAsync(Guid organisationId, StaffRole needed, CancellationToken cancellationToken)
    {
        if (!await CanAsync(organisationId, needed, cancellationToken))
        {
            throw new ForbiddenException(needed == StaffRole.Organiser
                ? "Only an organiser of this organisation, or of one above it, can do that."
                : "Only a referee or organiser of this organisation, or of one above it, can do that.");
        }
    }

    /// <summary>Only site administrators can do this.</summary>
    public void EnsureAdmin()
    {
        if (!user.IsAdmin)
        {
            throw new ForbiddenException("Only an administrator can do that.");
        }
    }
}
