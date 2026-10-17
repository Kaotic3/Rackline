using System.ComponentModel.DataAnnotations;
using Rackline.Domain.Organisations;

namespace Rackline.Web.Api;

/// <summary>A null parent creates a top-level organisation, which only an administrator can do.</summary>
public sealed record CreateOrganisationRequest(
    [Required, StringLength(200, MinimumLength = 1)] string Name,
    [Required] OrganisationLevel? Level,
    Guid? ParentId);

public sealed record RenameOrganisationRequest([Required, StringLength(200, MinimumLength = 1)] string Name);

public sealed record AddVenueRequest(
    [Required, StringLength(200, MinimumLength = 1)] string Name,
    [StringLength(500)] string? Address);

public sealed record AddTableRequest([Required, StringLength(50, MinimumLength = 1)] string Label);

public sealed record AppointStaffRequest(
    [Required, EmailAddress, StringLength(256)] string Email,
    [Required] StaffRole? Role);

public sealed record CreatePlayerRequest(
    [Required] Guid? OrganisationId,
    [Required, StringLength(100, MinimumLength = 1)] string FirstName,
    [Required, StringLength(100, MinimumLength = 1)] string LastName,
    [Required, RegularExpression("^[A-Za-z]{2}$", ErrorMessage = "Nationality must be a two-letter country code.")] string Nationality,
    [StringLength(50)] string? LicenceNumber);

public sealed record CreatedResponse(Guid Id);
