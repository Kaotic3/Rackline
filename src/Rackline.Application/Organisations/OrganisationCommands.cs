using Rackline.Application.Common;
using Rackline.Domain.Organisations;
using Rackline.Domain.Players;

namespace Rackline.Application.Organisations;

/// <summary>Loads and saves organisations, their venues and their staff appointments.</summary>
public interface IOrganisationRepository
{
    Task<Organisation?> FindAsync(Guid id, CancellationToken cancellationToken);

    void Add(Organisation organisation);

    /// <summary>The venue with its tables, tracked for changes.</summary>
    Task<Venue?> FindVenueAsync(Guid id, CancellationToken cancellationToken);

    void Add(Venue venue);

    Task<Appointment?> FindAppointmentAsync(Guid organisationId, Guid userId, CancellationToken cancellationToken);

    void Add(Appointment appointment);

    void Remove(Appointment appointment);

    /// <summary>Saves all changes. Throws <see cref="ConflictException"/> if someone else saved first.</summary>
    Task SaveChangesAsync(CancellationToken cancellationToken);
}

public interface IPlayerRepository
{
    Task<Player?> FindAsync(Guid id, CancellationToken cancellationToken);

    void Add(Player player);

    Task SaveChangesAsync(CancellationToken cancellationToken);
}

/// <summary>Sign-in accounts, which live with Identity rather than in the domain.</summary>
public interface IUserDirectory
{
    Task<Guid?> FindIdByEmailAsync(string email, CancellationToken cancellationToken);
}

/// <summary>
/// Building the hierarchy and its venues. Only an administrator can add a top-level organisation;
/// an organiser can add organisations below their own.
/// </summary>
public sealed class OrganisationSetup(IOrganisationRepository organisations, OrganisationAccess access)
{
    public async Task<Guid> CreateAsync(string name, OrganisationLevel level, Guid? parentId, CancellationToken cancellationToken = default)
    {
        Organisation? parent = null;
        if (parentId is { } id)
        {
            parent = await organisations.FindAsync(id, cancellationToken) ?? throw new NotFoundException("Organisation", id);
            await access.EnsureAsync(id, StaffRole.Organiser, cancellationToken);
        }
        else
        {
            access.EnsureAdmin();
        }

        var organisation = new Organisation(name, level, parent);
        organisations.Add(organisation);
        await organisations.SaveChangesAsync(cancellationToken);
        return organisation.Id;
    }

    public async Task RenameAsync(Guid organisationId, string name, CancellationToken cancellationToken = default)
    {
        var organisation = await organisations.FindAsync(organisationId, cancellationToken) ?? throw new NotFoundException("Organisation", organisationId);
        await access.EnsureAsync(organisationId, StaffRole.Organiser, cancellationToken);
        organisation.Rename(name);
        await organisations.SaveChangesAsync(cancellationToken);
    }

    public async Task<Guid> AddVenueAsync(Guid organisationId, string name, string? address, CancellationToken cancellationToken = default)
    {
        await access.EnsureAsync(organisationId, StaffRole.Organiser, cancellationToken);
        var venue = new Venue(organisationId, name, address);
        organisations.Add(venue);
        await organisations.SaveChangesAsync(cancellationToken);
        return venue.Id;
    }

    public async Task<Guid> AddTableAsync(Guid venueId, string label, CancellationToken cancellationToken = default)
    {
        var venue = await organisations.FindVenueAsync(venueId, cancellationToken) ?? throw new NotFoundException("Venue", venueId);
        await access.EnsureAsync(venue.OrganisationId, StaffRole.Organiser, cancellationToken);
        var table = venue.AddTable(label);
        await organisations.SaveChangesAsync(cancellationToken);
        return table.Id;
    }
}

/// <summary>
/// Appointing organisers and referees. An organiser can appoint staff at their organisation or any
/// below it, including other organisers: they can share their rights but never gain more.
/// </summary>
public sealed class StaffAppointments(
    IOrganisationRepository organisations,
    IUserDirectory users,
    IOrganisationQueries queries,
    OrganisationAccess access)
{
    /// <summary>Staff appointed at the organisation. The list holds email addresses, so only its organisers may read it.</summary>
    public async Task<IReadOnlyList<StaffDto>> ListAsync(Guid organisationId, CancellationToken cancellationToken = default)
    {
        await access.EnsureAsync(organisationId, StaffRole.Organiser, cancellationToken);
        return await queries.StaffAsync(organisationId, cancellationToken);
    }

    /// <summary>Appoints the user with that email, or changes their role if they already have one here.</summary>
    public async Task<Guid> AppointAsync(Guid organisationId, string email, StaffRole role, CancellationToken cancellationToken = default)
    {
        await access.EnsureAsync(organisationId, StaffRole.Organiser, cancellationToken);
        var userId = await users.FindIdByEmailAsync(email, cancellationToken) ?? throw new NotFoundException("User", email);

        if (await organisations.FindAppointmentAsync(organisationId, userId, cancellationToken) is { } existing)
        {
            existing.ChangeRole(role);
        }
        else
        {
            organisations.Add(new Appointment(organisationId, userId, role));
        }

        await organisations.SaveChangesAsync(cancellationToken);
        return userId;
    }

    /// <summary>Ends the user's appointment at this organisation. Appointments elsewhere are untouched.</summary>
    public async Task RemoveAsync(Guid organisationId, Guid userId, CancellationToken cancellationToken = default)
    {
        await access.EnsureAsync(organisationId, StaffRole.Organiser, cancellationToken);
        var appointment = await organisations.FindAppointmentAsync(organisationId, userId, cancellationToken)
            ?? throw new NotFoundException("Appointment", userId);
        organisations.Remove(appointment);
        await organisations.SaveChangesAsync(cancellationToken);
    }
}

/// <summary>Adding players and managing which organisations they belong to.</summary>
public sealed class PlayerRegistry(IPlayerRepository players, OrganisationAccess access)
{
    /// <summary>Adds a player as a member of the organisation.</summary>
    public async Task<Guid> CreateAsync(
        Guid organisationId,
        string firstName,
        string lastName,
        string nationality,
        string? licenceNumber,
        CancellationToken cancellationToken = default)
    {
        await access.EnsureAsync(organisationId, StaffRole.Organiser, cancellationToken);
        var player = new Player(firstName, lastName, nationality, licenceNumber);
        player.JoinOrganisation(organisationId);
        players.Add(player);
        await players.SaveChangesAsync(cancellationToken);
        return player.Id;
    }

    public Task AddMemberAsync(Guid organisationId, Guid playerId, CancellationToken cancellationToken = default) =>
        ChangeAsync(organisationId, playerId, p => p.JoinOrganisation(organisationId), cancellationToken);

    public Task RemoveMemberAsync(Guid organisationId, Guid playerId, CancellationToken cancellationToken = default) =>
        ChangeAsync(organisationId, playerId, p => p.LeaveOrganisation(organisationId), cancellationToken);

    private async Task ChangeAsync(Guid organisationId, Guid playerId, Action<Player> change, CancellationToken cancellationToken)
    {
        await access.EnsureAsync(organisationId, StaffRole.Organiser, cancellationToken);
        var player = await players.FindAsync(playerId, cancellationToken) ?? throw new NotFoundException("Player", playerId);
        change(player);
        await players.SaveChangesAsync(cancellationToken);
    }
}
