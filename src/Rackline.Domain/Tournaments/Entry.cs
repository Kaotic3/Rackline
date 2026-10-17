using Rackline.Domain.Common;

namespace Rackline.Domain.Tournaments;

/// <summary>A player's registration in a tournament.</summary>
public sealed class Entry : Entity
{
    /// <summary>Used by EF Core when loading.</summary>
    private Entry()
    {
    }

    internal Entry(Guid tournamentId, Guid playerId, int? seed, int handicap)
    {
        TournamentId = tournamentId;
        PlayerId = Guard.NotEmpty(playerId, "Player");
        Seed = seed;
        Handicap = handicap;
    }

    public Guid TournamentId { get; private set; }

    public Guid PlayerId { get; private set; }

    /// <summary>1 is the top seed; null means unseeded.</summary>
    public int? Seed { get; private set; }

    public int Handicap { get; private set; }

    internal void SetSeed(int? seed) => Seed = seed;

    internal void SetHandicap(int handicap) => Handicap = handicap;
}
