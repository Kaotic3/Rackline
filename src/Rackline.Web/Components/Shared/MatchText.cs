using Rackline.Application.Tournaments;
using Rackline.Domain.Matches;
using Rackline.Domain.Tournaments;

namespace Rackline.Web.Components.Shared;

/// <summary>Plain-English descriptions of matches and the players in them.</summary>
public static class MatchText
{
    public static string Round(TournamentDetailDto tournament, MatchDto match) => match.Side switch
    {
        BracketSide.GrandFinal => match.Round == 1 ? "Grand final" : "Grand final (reset)",
        BracketSide.Losers => $"Losers' round {match.Round}",
        BracketSide.Group => $"Group {(char)('A' + (match.Group ?? 1) - 1)} · round {match.Round}",
        _ when tournament.Format == TournamentFormat.DoubleElimination => $"Winners' round {match.Round}",
        _ => KnockoutRound(tournament, match.Round),
    };

    public static string Player(TournamentDetailDto tournament, SlotDto slot) => slot.Kind switch
    {
        MatchSlotKind.Entry => tournament.Entries.FirstOrDefault(e => e.Id == slot.EntryId)?.PlayerName ?? "Unknown player",
        MatchSlotKind.Bye => "Bye",
        _ => "To be decided",
    };

    public static string Length(MatchLengthDto length) =>
        length.Kind == Domain.Common.MatchLengthKind.RaceTo ? $"Race to {length.Value}" : $"Best of {length.Value}";

    private static string KnockoutRound(TournamentDetailDto tournament, int round)
    {
        var last = tournament.Matches.Where(m => m.Side == BracketSide.Winners).Select(m => m.Round).DefaultIfEmpty(round).Max();
        return (last - round) switch
        {
            0 => "Final",
            1 => "Semi-final",
            2 => "Quarter-final",
            _ => $"Round {round}",
        };
    }
}

public static class MatchDtoExtensions
{
    /// <summary>Settled without play because one or both slots were byes.</summary>
    public static bool IsByeLike(this MatchDto match) =>
        match.State == MatchState.Walkover && (match.Slot1.Kind == MatchSlotKind.Bye || match.Slot2.Kind == MatchSlotKind.Bye);

    /// <summary>Neither slot will ever hold a player: an empty bye pairing or an unused grand-final reset.</summary>
    public static bool IsVoid(this MatchDto match) =>
        match.State == MatchState.Walkover && match.Slot1.Kind == MatchSlotKind.Bye && match.Slot2.Kind == MatchSlotKind.Bye;
}
