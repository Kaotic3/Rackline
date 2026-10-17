using Rackline.Domain.Common;
using Rackline.Domain.Matches;
using Rackline.Domain.Tournaments;

namespace Rackline.Application.Tournaments;

public sealed record TournamentSummaryDto(
    Guid Id,
    Guid OrganisationId,
    string Name,
    Discipline Discipline,
    TournamentFormat Format,
    TournamentStatus Status,
    int EntryCount);

public sealed record TournamentDetailDto(
    Guid Id,
    Guid OrganisationId,
    string Name,
    Discipline Discipline,
    TournamentFormat Format,
    TournamentStatus Status,
    MatchLengthDto MatchLength,
    bool GrandFinalReset,
    int GroupCount,
    IReadOnlyList<EntryDto> Entries,
    IReadOnlyList<MatchDto> Matches,
    Guid? RankingId = null,
    string? RankingName = null);

public sealed record MatchLengthDto(MatchLengthKind Kind, int Value)
{
    public int FramesToWin => Kind == MatchLengthKind.RaceTo ? Value : (Value + 1) / 2;
}

public sealed record EntryDto(Guid Id, Guid PlayerId, string PlayerName, int? Seed, int Handicap);

public sealed record SlotDto(MatchSlotKind Kind, Guid? EntryId);

public sealed record SlotLinkDto(int MatchNumber, int Slot);

public sealed record MatchDto(
    Guid Id,
    int Number,
    BracketSide Side,
    int Round,
    int Position,
    int? Group,
    SlotDto Slot1,
    SlotDto Slot2,
    int Score1,
    int Score2,
    MatchState State,
    Guid? WinnerEntryId,
    Guid? TableId,
    DateTimeOffset? StartedAt,
    DateTimeOffset? CompletedAt,
    SlotLinkDto? WinnerTo,
    SlotLinkDto? LoserTo,
    int BreakAndRuns1,
    int BreakAndRuns2)
{
    public static MatchDto From(Match match) => new(
        match.Id,
        match.Number,
        match.Side,
        match.Round,
        match.Position,
        match.Group,
        new SlotDto(match.Slot1.Kind, match.Slot1.EntryId),
        new SlotDto(match.Slot2.Kind, match.Slot2.EntryId),
        match.Score1,
        match.Score2,
        match.State,
        match.WinnerEntryId,
        match.TableId,
        match.StartedAt,
        match.CompletedAt,
        match.WinnerTo is { } w ? new SlotLinkDto(w.MatchNumber, w.Slot) : null,
        match.LoserTo is { } l ? new SlotLinkDto(l.MatchNumber, l.Slot) : null,
        match.Frames.Count(f => f.BreakAndRun && f.WinnerSlot == 1),
        match.Frames.Count(f => f.BreakAndRun && f.WinnerSlot == 2));
}

/// <summary>Read-only views of tournaments, projected straight from storage.</summary>
public interface ITournamentQueries
{
    Task<IReadOnlyList<TournamentSummaryDto>> ListAsync(TournamentStatus? status, CancellationToken cancellationToken);

    Task<TournamentDetailDto?> GetAsync(Guid id, CancellationToken cancellationToken);

    /// <summary>The tournament a match belongs to, or null if there is no such match.</summary>
    Task<Guid?> FindTournamentIdForMatchAsync(Guid matchId, CancellationToken cancellationToken);
}
