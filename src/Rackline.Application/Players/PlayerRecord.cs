namespace Rackline.Application.Players;

/// <summary>Totals over a set of finished matches. Walkovers count as wins and losses but add no frames.</summary>
public sealed record PlayerRecord(
    int Played,
    int Won,
    int Lost,
    int Walkovers,
    int FramesFor,
    int FramesAgainst,
    int BreakAndRuns)
{
    public int FrameDifference => FramesFor - FramesAgainst;

    /// <summary>Share of matches won, 0–100, or null before any are played.</summary>
    public int? WinPercentage => Played == 0 ? null : (int)Math.Round(100.0 * Won / Played, MidpointRounding.AwayFromZero);

    public static PlayerRecord From(IEnumerable<PlayedMatchDto> matches)
    {
        var list = matches.ToList();
        return new PlayerRecord(
            list.Count,
            list.Count(m => m.Won),
            list.Count(m => !m.Won),
            list.Count(m => m.Walkover),
            list.Sum(m => m.FramesFor),
            list.Sum(m => m.FramesAgainst),
            list.Sum(m => m.BreakAndRunsFor));
    }
}

/// <summary>Two players' meetings, seen from the first player's side.</summary>
public sealed record HeadToHead(Guid PlayerId, Guid OpponentId, PlayerRecord Record, IReadOnlyList<PlayedMatchDto> Meetings)
{
    /// <param name="playerMatches">All finished matches of <paramref name="playerId"/>.</param>
    public static HeadToHead Between(Guid playerId, Guid opponentId, IEnumerable<PlayedMatchDto> playerMatches)
    {
        var meetings = playerMatches.Where(m => m.OpponentPlayerId == opponentId).ToList();
        return new HeadToHead(playerId, opponentId, PlayerRecord.From(meetings), meetings);
    }
}
