using Rackline.Domain.Common;

namespace Rackline.Domain.Rankings;

/// <summary>
/// Points table for one organisation, discipline and season. Tournaments that count towards it award
/// points by finishing position when they are completed.
/// </summary>
public sealed class Ranking : Entity
{
    public const int MaxPositions = 64;

    private readonly List<RankingAward> _awards = [];
    private readonly List<int> _pointsByPosition = [];

    /// <summary>Used by EF Core when loading.</summary>
    private Ranking()
    {
        Name = null!;
        Season = null!;
    }

    /// <param name="pointsByPosition">Points for first place, second place and so on.</param>
    /// <param name="participationPoints">Points for finishing below the last position in the list.</param>
    public Ranking(Guid organisationId, string name, Discipline discipline, string season, IReadOnlyList<int> pointsByPosition, int participationPoints = 0)
    {
        OrganisationId = Guard.NotEmpty(organisationId, "Organisation");
        Name = Guard.NotBlank(name, "Ranking name");
        Discipline = discipline;
        Season = Guard.NotBlank(season, "Season");

        Guard.That(pointsByPosition.Count is > 0 and <= MaxPositions, $"Give points for between 1 and {MaxPositions} positions.");
        Guard.That(pointsByPosition.All(p => p >= 0) && participationPoints >= 0, "Points must not be negative.");
        Guard.That(
            pointsByPosition.Zip(pointsByPosition.Skip(1)).All(pair => pair.First >= pair.Second)
            && participationPoints <= pointsByPosition[^1],
            "A higher position must never be worth fewer points than a lower one.");

        _pointsByPosition.AddRange(pointsByPosition);
        ParticipationPoints = participationPoints;
    }

    public Guid OrganisationId { get; private set; }

    public string Name { get; private set; }

    public Discipline Discipline { get; private set; }

    public string Season { get; private set; }

    public IReadOnlyList<int> PointsByPosition => _pointsByPosition;

    public int ParticipationPoints { get; private set; }

    public IReadOnlyList<RankingAward> Awards => _awards;

    public int PointsFor(int position)
    {
        Guard.Positive(position, "Position");
        return position <= _pointsByPosition.Count ? _pointsByPosition[position - 1] : ParticipationPoints;
    }

    public bool HasAwarded(Guid tournamentId) => _awards.Any(a => a.TournamentId == tournamentId);

    /// <summary>Awards points to every player in a finished tournament. A tournament can count only once.</summary>
    public void AwardTournament(Guid tournamentId, IEnumerable<(Guid PlayerId, int Position)> placings)
    {
        Guard.NotEmpty(tournamentId, "Tournament");
        Guard.That(!HasAwarded(tournamentId), "That tournament has already been counted in this ranking.");

        var list = placings.ToList();
        Guard.That(list.Select(p => p.PlayerId).Distinct().Count() == list.Count, "A player can be placed only once in a tournament.");

        _awards.AddRange(list.Select(p => new RankingAward(tournamentId, Guard.NotEmpty(p.PlayerId, "Player"), p.Position, PointsFor(p.Position))));
    }

    public int TotalFor(Guid playerId) => _awards.Where(a => a.PlayerId == playerId).Sum(a => a.Points);

    public IReadOnlyList<RankingRow> Table() => RankingTable.Calculate(_awards.Select(a => (a.PlayerId, a.Points)));
}

/// <summary>The points one player earned from one tournament.</summary>
public sealed class RankingAward
{
    /// <summary>Used by EF Core when loading.</summary>
    private RankingAward()
    {
    }

    internal RankingAward(Guid tournamentId, Guid playerId, int position, int points)
    {
        TournamentId = tournamentId;
        PlayerId = playerId;
        Position = position;
        Points = points;
    }

    public Guid TournamentId { get; private set; }

    public Guid PlayerId { get; private set; }

    public int Position { get; private set; }

    public int Points { get; private set; }
}

public sealed record RankingRow(int Position, Guid PlayerId, int Points, int Tournaments);

/// <summary>Totals per player, highest first. Players on equal points share a position.</summary>
public static class RankingTable
{
    public static IReadOnlyList<RankingRow> Calculate(IEnumerable<(Guid PlayerId, int Points)> awards)
    {
        var totals = awards
            .GroupBy(a => a.PlayerId)
            .Select(g => (PlayerId: g.Key, Points: g.Sum(a => a.Points), Tournaments: g.Count()))
            .OrderByDescending(t => t.Points)
            .ThenBy(t => t.PlayerId)
            .ToList();

        return [.. totals.Select(t => new RankingRow(1 + totals.Count(o => o.Points > t.Points), t.PlayerId, t.Points, t.Tournaments))];
    }
}
