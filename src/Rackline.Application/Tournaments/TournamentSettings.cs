using Rackline.Domain.Common;
using Rackline.Domain.Tournaments;

namespace Rackline.Application.Tournaments;

/// <summary>Everything an organiser chooses about how a tournament is played, and the ranking it counts towards.</summary>
public sealed record TournamentSettings(
    string Name,
    Discipline Discipline,
    TournamentFormat Format,
    MatchLengthKind MatchLengthKind,
    int MatchLengthValue,
    bool GrandFinalReset = true,
    int GroupCount = 1,
    Guid? RankingId = null)
{
    internal MatchLength ToMatchLength() => MatchLengthKind == MatchLengthKind.RaceTo
        ? MatchLength.RaceTo(MatchLengthValue)
        : MatchLength.BestOf(MatchLengthValue);

    internal FormatOptions ToOptions() => new(GrandFinalReset, GroupCount);
}
