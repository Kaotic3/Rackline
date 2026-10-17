using Rackline.Domain.Matches;
using Rackline.Domain.Standings;

namespace Rackline.Application.Tournaments;

public sealed record StandingRowDto(
    int Position,
    Guid EntryId,
    Guid PlayerId,
    string PlayerName,
    int Played,
    int Won,
    int Lost,
    int FramesFor,
    int FramesAgainst)
{
    public int FrameDifference => FramesFor - FramesAgainst;
}

public sealed record GroupTableDto(int Group, IReadOnlyList<StandingRowDto> Rows);

/// <summary>Round-robin tables for a tournament, using the domain's ranking rules.</summary>
public static class GroupTables
{
    public static IReadOnlyList<GroupTableDto> For(TournamentDetailDto tournament)
    {
        var entries = tournament.Entries.ToDictionary(e => e.Id);

        return
        [
            .. tournament.Matches
                .Where(m => m.Side == BracketSide.Group && m.Group is not null)
                .GroupBy(m => m.Group!.Value)
                .OrderBy(g => g.Key)
                .Select(group =>
                {
                    // The draw order is not stored, so seeds (then entry ids) give the final tie-break,
                    // as in the domain's finishing positions.
                    var members = group
                        .SelectMany(m => new[] { m.Slot1.EntryId, m.Slot2.EntryId })
                        .OfType<Guid>()
                        .Distinct()
                        .OrderBy(id => entries[id].Seed ?? int.MaxValue)
                        .ThenBy(id => id)
                        .ToList();

                    var rows = GroupStandings.Calculate(members, group.Select(ToResult).OfType<GroupResult>())
                        .Select(r => new StandingRowDto(
                            r.Position, r.EntryId, entries[r.EntryId].PlayerId, entries[r.EntryId].PlayerName,
                            r.Played, r.Won, r.Lost, r.FramesFor, r.FramesAgainst))
                        .ToList();

                    return new GroupTableDto(group.Key, rows);
                }),
        ];
    }

    private static GroupResult? ToResult(MatchDto match)
    {
        if (match.State is not (MatchState.Completed or MatchState.Walkover)
            || match.WinnerEntryId is not { } winner
            || match.Slot1.EntryId is not { } one
            || match.Slot2.EntryId is not { } two)
        {
            return null;
        }

        var winnerIsSlot1 = winner == one;
        var walkover = match.State == MatchState.Walkover;
        return new GroupResult(
            winner,
            winnerIsSlot1 ? two : one,
            walkover ? 0 : winnerIsSlot1 ? match.Score1 : match.Score2,
            walkover ? 0 : winnerIsSlot1 ? match.Score2 : match.Score1,
            walkover);
    }
}
