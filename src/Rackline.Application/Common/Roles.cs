namespace Rackline.Application.Common;

public static class Roles
{
    public const string Admin = "Admin";
    public const string Organiser = "Organiser";
    public const string Referee = "Referee";
    public const string Player = "Player";

    /// <summary>Every role with a fixed id, so roles can be created by a migration.</summary>
    public static IReadOnlyList<(Guid Id, string Name)> All { get; } =
    [
        (new Guid("0193a000-0000-7000-8000-000000000001"), Admin),
        (new Guid("0193a000-0000-7000-8000-000000000002"), Organiser),
        (new Guid("0193a000-0000-7000-8000-000000000003"), Referee),
        (new Guid("0193a000-0000-7000-8000-000000000004"), Player),
    ];
}

public static class Policies
{
    /// <summary>Create, change and run tournaments: Admin or Organiser.</summary>
    public const string ManageTournaments = nameof(ManageTournaments);

    /// <summary>Start matches and record results: Admin, Organiser or Referee.</summary>
    public const string ScoreMatches = nameof(ScoreMatches);

    /// <summary>Set up leagues and seasons, and enter fixture results: Admin or Organiser.</summary>
    public const string ManageLeagues = nameof(ManageLeagues);

    /// <summary>Set up rankings: Admin or Organiser.</summary>
    public const string ManageRankings = nameof(ManageRankings);

    /// <summary>Work out every rating again from the full history: Admin only.</summary>
    public const string RebuildRatings = nameof(RebuildRatings);

    /// <summary>Assign roles to users: Admin only.</summary>
    public const string ManageUsers = nameof(ManageUsers);
}
