using Rackline.Domain.Common;
using Rackline.Domain.Draws;

namespace Rackline.Domain.Tournaments;

public enum TournamentFormat
{
    SingleElimination,
    DoubleElimination,
    RoundRobin,
    GroupsThenKnockout,
    Swiss,
}

public enum TournamentStatus
{
    Draft,
    Registration,
    Drawn,
    InProgress,
    Completed,
    Cancelled,
}

/// <summary>Format-specific settings.</summary>
public sealed record FormatOptions(bool GrandFinalReset = true, int GroupCount = 1)
{
    public static FormatOptions Default { get; } = new();

    internal IDrawEngine CreateEngine(TournamentFormat format) => format switch
    {
        TournamentFormat.SingleElimination => new SingleEliminationEngine(),
        TournamentFormat.DoubleElimination => new DoubleEliminationEngine(GrandFinalReset),
        TournamentFormat.RoundRobin => new RoundRobinEngine(GroupCount),
        _ => throw new DomainException($"The {format} format is not supported yet."),
    };
}
