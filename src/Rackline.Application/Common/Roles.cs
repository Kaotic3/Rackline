namespace Rackline.Application.Common;

/// <summary>
/// Site-wide roles. Organisers and referees are not site-wide: they are appointed at an organisation
/// (see <see cref="Domain.Organisations.Appointment"/>).
/// </summary>
public static class Roles
{
    public const string Admin = "Admin";
    public const string Player = "Player";

    /// <summary>Every role with a fixed id, so roles can be created by a migration.</summary>
    public static IReadOnlyList<(Guid Id, string Name)> All { get; } =
    [
        (new Guid("0193a000-0000-7000-8000-000000000001"), Admin),
        (new Guid("0193a000-0000-7000-8000-000000000004"), Player),
    ];

    /// <summary>The ids the retired site-wide Organiser and Referee roles had, for the migration that replaced them.</summary>
    public static readonly Guid RetiredOrganiserId = new("0193a000-0000-7000-8000-000000000002");

    /// <inheritdoc cref="RetiredOrganiserId"/>
    public static readonly Guid RetiredRefereeId = new("0193a000-0000-7000-8000-000000000003");
}

public static class Policies
{
    // Staff policies only check that the user is an admin or holds a high enough appointment
    // somewhere; each command then checks the organisation it touches.

    /// <summary>Create, change and run tournaments: Admin or Organiser.</summary>
    public const string ManageTournaments = nameof(ManageTournaments);

    /// <summary>Start matches and record results: Admin, Organiser or Referee.</summary>
    public const string ScoreMatches = nameof(ScoreMatches);

    /// <summary>Set up leagues and seasons, and enter fixture results: Admin or Organiser.</summary>
    public const string ManageLeagues = nameof(ManageLeagues);

    /// <summary>Set up rankings: Admin or Organiser.</summary>
    public const string ManageRankings = nameof(ManageRankings);

    /// <summary>Add organisations, venues, players and staff: Admin or Organiser.</summary>
    public const string ManageOrganisations = nameof(ManageOrganisations);

    /// <summary>Work out every rating again from the full history: Admin only.</summary>
    public const string RebuildRatings = nameof(RebuildRatings);

    /// <summary>Assign roles to users: Admin only.</summary>
    public const string ManageUsers = nameof(ManageUsers);
}
