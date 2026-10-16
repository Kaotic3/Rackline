namespace Rackline.Domain.Matches;

public enum MatchState
{
    Pending,
    Ready,
    Live,
    Completed,
    Walkover,
}

public enum BracketSide
{
    /// <summary>Single-elimination bracket, or the winners' bracket in double elimination.</summary>
    Winners,
    Losers,
    GrandFinal,
    Group,
}
