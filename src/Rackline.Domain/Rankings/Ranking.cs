using Rackline.Domain.Common;

namespace Rackline.Domain.Rankings;

/// <summary>Points table for one organisation, discipline and season.</summary>
public sealed class Ranking : Entity
{
    private readonly List<RankingEntry> _entries = [];

    /// <summary>Used by EF Core when loading.</summary>
    private Ranking()
    {
        Season = null!;
    }

    public Ranking(Guid organisationId, Discipline discipline, string season)
    {
        OrganisationId = Guard.NotEmpty(organisationId, "Organisation");
        Discipline = discipline;
        Season = Guard.NotBlank(season, "Season");
    }

    public Guid OrganisationId { get; private set; }

    public Discipline Discipline { get; private set; }

    public string Season { get; private set; }

    public IReadOnlyList<RankingEntry> Entries => _entries;

    public void AwardPoints(Guid playerId, int points)
    {
        Guard.NotEmpty(playerId, "Player");
        Guard.Positive(points, "Points");

        var entry = _entries.SingleOrDefault(e => e.PlayerId == playerId);
        if (entry is null)
        {
            _entries.Add(new RankingEntry(playerId, points));
        }
        else
        {
            entry.Add(points);
        }
    }

    public int PointsFor(Guid playerId) => _entries.SingleOrDefault(e => e.PlayerId == playerId)?.Points ?? 0;

    /// <summary>Players by points, highest first; equal points share a position.</summary>
    public IReadOnlyList<(int Position, Guid PlayerId, int Points)> Table()
    {
        var ordered = _entries.OrderByDescending(e => e.Points).ToList();
        return [.. ordered.Select(e => (ordered.FindIndex(o => o.Points == e.Points) + 1, e.PlayerId, e.Points))];
    }
}

/// <summary>One player's running total in a ranking.</summary>
public sealed class RankingEntry
{
    /// <summary>Used by EF Core when loading.</summary>
    private RankingEntry()
    {
    }

    internal RankingEntry(Guid playerId, int points)
    {
        PlayerId = playerId;
        Points = points;
    }

    public Guid PlayerId { get; private set; }

    public int Points { get; private set; }

    internal void Add(int points) => Points += points;
}
