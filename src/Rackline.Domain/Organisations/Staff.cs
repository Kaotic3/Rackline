using Rackline.Domain.Common;

namespace Rackline.Domain.Organisations;

/// <summary>What a member of staff may do. Each role includes the ones before it.</summary>
public enum StaffRole
{
    /// <summary>Starts matches and records results.</summary>
    Referee,

    /// <summary>Runs tournaments, leagues and rankings, and appoints staff.</summary>
    Organiser,
}

/// <summary>A user's role at one organisation. It also covers every organisation below it.</summary>
public sealed class Appointment : Entity
{
    /// <summary>Used by EF Core when loading.</summary>
    private Appointment()
    {
    }

    public Appointment(Guid organisationId, Guid userId, StaffRole role)
    {
        OrganisationId = Guard.NotEmpty(organisationId, "Organisation");
        UserId = Guard.NotEmpty(userId, "User");
        Role = role;
    }

    public Guid OrganisationId { get; private set; }

    public Guid UserId { get; private set; }

    public StaffRole Role { get; private set; }

    public void ChangeRole(StaffRole role) => Role = role;
}

/// <summary>An organisation and everything above it: itself first, then its parent, up to the top.</summary>
public sealed class OrganisationLineage
{
    private readonly List<Guid> _ids;

    /// <param name="parents">The parent of every organisation that may be on the way up.</param>
    public OrganisationLineage(Guid organisationId, IReadOnlyDictionary<Guid, Guid?> parents)
    {
        Guard.That(parents.ContainsKey(organisationId), "Unknown organisation.");
        _ids = [organisationId];
        while (parents[_ids[^1]] is { } parentId)
        {
            Guard.That(parents.ContainsKey(parentId), "Unknown parent organisation.");
            Guard.That(!_ids.Contains(parentId), "The organisation hierarchy has a loop.");
            _ids.Add(parentId);
        }
    }

    public Guid Self => _ids[0];

    /// <summary>Itself first, then its parent, up to the top.</summary>
    public IReadOnlyList<Guid> Ids => _ids;

    /// <summary>True for the organisation itself and every organisation above it.</summary>
    public bool Includes(Guid organisationId) => _ids.Contains(organisationId);
}

/// <summary>Who may do what, given a user's appointments.</summary>
public static class StaffPermissions
{
    /// <summary>
    /// True when one of the appointments is at the organisation or above it in the hierarchy, with
    /// at least the role needed. An organiser can do everything a referee can.
    /// </summary>
    public static bool Allows(IEnumerable<Appointment> appointments, OrganisationLineage target, StaffRole needed) =>
        appointments.Any(a => a.Role >= needed && target.Includes(a.OrganisationId));
}
