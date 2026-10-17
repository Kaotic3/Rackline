using Rackline.Domain.Common;
using Rackline.Domain.Rankings;
using Rackline.Domain.Tournaments;

namespace Rackline.Application.Rankings;

public sealed record RankingSummaryDto(
    Guid Id,
    Guid OrganisationId,
    string OrganisationName,
    string Name,
    Discipline Discipline,
    string Season,
    int TournamentsCounted);

public sealed record RankingRowDto(int Position, Guid PlayerId, string PlayerName, int Points, int Tournaments);

/// <summary>A tournament that counts towards a ranking; <see cref="Counted"/> once its points are in.</summary>
public sealed record RankedTournamentDto(Guid Id, string Name, TournamentStatus Status, bool Counted);

public sealed record RankingDetailDto(
    Guid Id,
    Guid OrganisationId,
    string OrganisationName,
    string Name,
    Discipline Discipline,
    string Season,
    IReadOnlyList<int> PointsByPosition,
    int ParticipationPoints,
    IReadOnlyList<RankingRowDto> Table,
    IReadOnlyList<RankedTournamentDto> Tournaments);

/// <summary>Where a player stands in one ranking.</summary>
public sealed record PlayerRankingDto(Guid RankingId, string Name, Discipline Discipline, string Season, int Position, int Points);

public interface IRankingQueries
{
    Task<IReadOnlyList<RankingSummaryDto>> ListAsync(CancellationToken cancellationToken);

    Task<RankingDetailDto?> GetAsync(Guid rankingId, CancellationToken cancellationToken);

    /// <summary>Every ranking in which the player has points.</summary>
    Task<IReadOnlyList<PlayerRankingDto>> ForPlayerAsync(Guid playerId, CancellationToken cancellationToken);
}

public sealed record RatingRowDto(int Position, Guid PlayerId, string PlayerName, string Nationality, double Value, int MatchesPlayed)
{
    public bool IsProvisional => MatchesPlayed < Elo.ProvisionalMatches;
}

/// <summary>How one rated match moved a player's rating, from that player's side.</summary>
public sealed record RatingChangeDto(
    Discipline Discipline,
    Guid MatchId,
    Guid? TournamentId,
    string? TournamentName,
    Guid OpponentId,
    string OpponentName,
    bool Won,
    double Before,
    double After,
    DateTimeOffset At)
{
    public double Change => After - Before;
}

public interface IRatingQueries
{
    /// <summary>Disciplines in which anyone has a rating.</summary>
    Task<IReadOnlyList<Discipline>> RatedDisciplinesAsync(CancellationToken cancellationToken);

    /// <summary>Every rated player in the discipline, highest first.</summary>
    Task<IReadOnlyList<RatingRowDto>> LeaderboardAsync(Discipline discipline, CancellationToken cancellationToken);

    /// <summary>The player's most recent rating changes, newest first.</summary>
    Task<IReadOnlyList<RatingChangeDto>> HistoryAsync(Guid playerId, int limit, CancellationToken cancellationToken);
}
