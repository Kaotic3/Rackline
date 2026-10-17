using Rackline.Domain.Common;
using Rackline.Domain.Leagues;

namespace Rackline.Application.Leagues;

public sealed record SeasonSummaryDto(Guid Id, string Name, DateOnly StartsOn, DateOnly EndsOn, SeasonStatus Status, int TeamCount);

public sealed record LeagueSummaryDto(Guid Id, Guid OrganisationId, string Name, Discipline Discipline, IReadOnlyList<SeasonSummaryDto> Seasons);

public sealed record SeasonRulesDto(
    int RubbersPerFixture,
    MatchLengthKind RubberLengthKind,
    int RubberLengthValue,
    int PointsForWin,
    int PointsForDraw,
    int PointsPerRubber)
{
    public int FramesToWin => RubberLengthKind == MatchLengthKind.RaceTo ? RubberLengthValue : (RubberLengthValue + 1) / 2;
}

public sealed record RosterPlayerDto(Guid Id, string Name);

public sealed record TeamDto(Guid Id, string Name, Guid? HomeVenueId, string? HomeVenueName, IReadOnlyList<RosterPlayerDto> Players);

public sealed record RubberDto(int Number, Guid HomePlayerId, Guid AwayPlayerId, int HomeScore, int AwayScore);

public sealed record FixtureDto(
    Guid Id,
    int Round,
    Guid HomeTeamId,
    Guid AwayTeamId,
    DateTimeOffset? ScheduledFor,
    FixtureStatus Status,
    DateTimeOffset? CompletedAt,
    IReadOnlyList<RubberDto> Rubbers)
{
    public int HomeRubbersWon => Rubbers.Count(r => r.HomeScore > r.AwayScore);

    public int AwayRubbersWon => Rubbers.Count(r => r.AwayScore > r.HomeScore);
}

public sealed record SeasonDetailDto(
    Guid Id,
    Guid LeagueId,
    string LeagueName,
    Discipline Discipline,
    string Name,
    DateOnly StartsOn,
    DateOnly EndsOn,
    SeasonStatus Status,
    SeasonRulesDto Rules,
    IReadOnlyList<TeamDto> Teams,
    IReadOnlyList<FixtureDto> Fixtures)
{
    /// <summary>Players who appear in rubbers but are no longer on any roster.</summary>
    public IReadOnlyList<RosterPlayerDto> FormerPlayers { get; init; } = [];

    public string TeamName(Guid teamId) => Teams.FirstOrDefault(t => t.Id == teamId)?.Name ?? "Unknown team";

    public string PlayerName(Guid playerId) =>
        Teams.SelectMany(t => t.Players).Concat(FormerPlayers).FirstOrDefault(p => p.Id == playerId)?.Name ?? "Unknown player";
}

public sealed record LeagueTableRowDto(
    int Position,
    Guid TeamId,
    string TeamName,
    int Played,
    int Won,
    int Drawn,
    int Lost,
    int RubbersFor,
    int RubbersAgainst,
    int Points)
{
    public int RubberDifference => RubbersFor - RubbersAgainst;
}

public interface ILeagueQueries
{
    Task<IReadOnlyList<LeagueSummaryDto>> ListAsync(CancellationToken cancellationToken);

    Task<LeagueSummaryDto?> GetAsync(Guid leagueId, CancellationToken cancellationToken);

    /// <summary>A season with its teams, rosters, fixtures and rubbers.</summary>
    Task<SeasonDetailDto?> GetSeasonAsync(Guid seasonId, CancellationToken cancellationToken);
}
