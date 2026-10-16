using Rackline.Domain.Common;

namespace Rackline.Domain.Rankings;

/// <summary>Points table for one organisation, discipline and season.</summary>
public sealed class Ranking : Entity
{
    private readonly Dictionary<Guid, int> _points = [];

    public Ranking(Guid organisationId, Discipline discipline, string season)
    {
        OrganisationId = Guard.NotEmpty(organisationId, "Organisation");
        Discipline = discipline;
        Season = Guard.NotBlank(season, "Season");
    }

    public Guid OrganisationId { get; private set; }

    public Discipline Discipline { get; private set; }

    public string Season { get; private set; }

    public void AwardPoints(Guid playerId, int points)
    {
        Guard.NotEmpty(playerId, "Player");
        Guard.Positive(points, "Points");
        _points[playerId] = _points.GetValueOrDefault(playerId) + points;
    }

    public int PointsFor(Guid playerId) => _points.GetValueOrDefault(playerId);

    /// <summary>Players by points, highest first; equal points share a position.</summary>
    public IReadOnlyList<(int Position, Guid PlayerId, int Points)> Table()
    {
        var ordered = _points.OrderByDescending(p => p.Value).ToList();
        return [.. ordered.Select(p => (ordered.FindIndex(o => o.Value == p.Value) + 1, p.Key, p.Value))];
    }
}
