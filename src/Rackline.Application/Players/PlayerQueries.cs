using Rackline.Domain.Common;
using Rackline.Domain.Rankings;

namespace Rackline.Application.Players;

public sealed record PlayerSummaryDto(Guid Id, string FirstName, string LastName, string Nationality, string? LicenceNumber)
{
    public string DisplayName => $"{FirstName} {LastName}";
}

public sealed record RatingDto(Discipline Discipline, double Value, int MatchesPlayed)
{
    public bool IsProvisional => MatchesPlayed < Elo.ProvisionalMatches;
}

public sealed record PlayerProfileDto(
    Guid Id,
    string FirstName,
    string LastName,
    string Nationality,
    string? LicenceNumber,
    IReadOnlyList<string> Organisations,
    IReadOnlyList<RatingDto> Ratings)
{
    public string DisplayName => $"{FirstName} {LastName}";
}

/// <summary>A finished match against another player, seen from one player's side.</summary>
public sealed record PlayedMatchDto(
    Guid MatchId,
    Guid TournamentId,
    string TournamentName,
    Discipline Discipline,
    DateTimeOffset? PlayedAt,
    Guid OpponentPlayerId,
    string OpponentName,
    bool Won,
    bool Walkover,
    int FramesFor,
    int FramesAgainst,
    int BreakAndRunsFor,
    int BreakAndRunsAgainst);

public interface IPlayerQueries
{
    /// <summary>Players whose name contains <paramref name="search"/> (case-insensitive), or all players.</summary>
    Task<IReadOnlyList<PlayerSummaryDto>> ListAsync(string? search, CancellationToken cancellationToken);

    Task<PlayerProfileDto?> GetProfileAsync(Guid playerId, CancellationToken cancellationToken);

    /// <summary>Every finished match the player has played against an opponent (byes excluded), newest first.</summary>
    Task<IReadOnlyList<PlayedMatchDto>> GetPlayedMatchesAsync(Guid playerId, CancellationToken cancellationToken);
}
