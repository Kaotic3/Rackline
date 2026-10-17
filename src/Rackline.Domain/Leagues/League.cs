using Rackline.Domain.Common;

namespace Rackline.Domain.Leagues;

/// <summary>A team competition run over one or more seasons.</summary>
public sealed class League : Entity
{
    private readonly List<Season> _seasons = [];

    /// <summary>Used by EF Core when loading.</summary>
    private League()
    {
        Name = null!;
    }

    public League(Guid organisationId, string name, Discipline discipline)
    {
        OrganisationId = Guard.NotEmpty(organisationId, "Organisation");
        Name = Guard.NotBlank(name, "League name");
        Discipline = discipline;
    }

    public Guid OrganisationId { get; private set; }

    public string Name { get; private set; }

    public Discipline Discipline { get; private set; }

    public IReadOnlyList<Season> Seasons => _seasons;

    public void Rename(string name) => Name = Guard.NotBlank(name, "League name");

    public Season AddSeason(string name, DateOnly startsOn, DateOnly endsOn, SeasonRules rules)
    {
        var season = new Season(Id, name, startsOn, endsOn, rules);
        Guard.That(
            _seasons.All(s => s.EndsOn < startsOn || s.StartsOn > endsOn),
            "Seasons in the same league must not overlap.");
        _seasons.Add(season);
        return season;
    }

    public Season FindSeason(Guid seasonId) =>
        _seasons.SingleOrDefault(s => s.Id == seasonId) ?? throw new DomainException("Season not found.");
}
