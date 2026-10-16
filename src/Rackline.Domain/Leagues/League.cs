using Rackline.Domain.Common;

namespace Rackline.Domain.Leagues;

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

    public Season AddSeason(string name, DateOnly startsOn, DateOnly endsOn)
    {
        var season = new Season(Id, name, startsOn, endsOn);
        Guard.That(
            _seasons.All(s => s.EndsOn < startsOn || s.StartsOn > endsOn),
            "Seasons in the same league must not overlap.");
        _seasons.Add(season);
        return season;
    }
}

public sealed class Season : Entity
{
    private readonly List<Team> _teams = [];
    private readonly List<Fixture> _fixtures = [];

    /// <summary>Used by EF Core when loading.</summary>
    private Season()
    {
        Name = null!;
    }

    internal Season(Guid leagueId, string name, DateOnly startsOn, DateOnly endsOn)
    {
        Guard.That(endsOn >= startsOn, "A season cannot end before it starts.");
        LeagueId = leagueId;
        Name = Guard.NotBlank(name, "Season name");
        StartsOn = startsOn;
        EndsOn = endsOn;
    }

    public Guid LeagueId { get; private set; }

    public string Name { get; private set; }

    public DateOnly StartsOn { get; private set; }

    public DateOnly EndsOn { get; private set; }

    public IReadOnlyList<Team> Teams => _teams;

    public IReadOnlyList<Fixture> Fixtures => _fixtures;

    public Team AddTeam(string name, Guid? homeVenueId = null)
    {
        name = Guard.NotBlank(name, "Team name");
        Guard.That(
            _teams.All(t => !string.Equals(t.Name, name, StringComparison.OrdinalIgnoreCase)),
            $"A team called '{name}' is already in this season.");
        var team = new Team(Id, name, homeVenueId);
        _teams.Add(team);
        return team;
    }

    public Fixture AddFixture(Guid homeTeamId, Guid awayTeamId, DateTimeOffset? scheduledFor = null)
    {
        Guard.That(homeTeamId != awayTeamId, "A team cannot play itself.");
        Guard.That(
            _teams.Any(t => t.Id == homeTeamId) && _teams.Any(t => t.Id == awayTeamId),
            "Both teams must be in this season.");
        var fixture = new Fixture(Id, homeTeamId, awayTeamId, scheduledFor);
        _fixtures.Add(fixture);
        return fixture;
    }
}

public sealed class Team : Entity
{
    private readonly List<Guid> _playerIds = [];

    /// <summary>Used by EF Core when loading.</summary>
    private Team()
    {
        Name = null!;
    }

    internal Team(Guid seasonId, string name, Guid? homeVenueId)
    {
        SeasonId = seasonId;
        Name = name;
        HomeVenueId = homeVenueId;
    }

    public Guid SeasonId { get; private set; }

    public string Name { get; private set; }

    public Guid? HomeVenueId { get; private set; }

    public IReadOnlyCollection<Guid> PlayerIds => _playerIds;

    public void AddPlayer(Guid playerId)
    {
        if (!_playerIds.Contains(Guard.NotEmpty(playerId, "Player")))
        {
            _playerIds.Add(playerId);
        }
    }

    public void RemovePlayer(Guid playerId) => _playerIds.Remove(playerId);
}

/// <summary>A team match, made up of individual rubbers.</summary>
public sealed class Fixture : Entity
{
    private readonly List<Rubber> _rubbers = [];

    /// <summary>Used by EF Core when loading.</summary>
    private Fixture()
    {
    }

    internal Fixture(Guid seasonId, Guid homeTeamId, Guid awayTeamId, DateTimeOffset? scheduledFor)
    {
        SeasonId = seasonId;
        HomeTeamId = homeTeamId;
        AwayTeamId = awayTeamId;
        ScheduledFor = scheduledFor;
    }

    public Guid SeasonId { get; private set; }

    public Guid HomeTeamId { get; private set; }

    public Guid AwayTeamId { get; private set; }

    public DateTimeOffset? ScheduledFor { get; private set; }

    public IReadOnlyList<Rubber> Rubbers => _rubbers;

    public int HomeRubbersWon => _rubbers.Count(r => r.HomeScore > r.AwayScore);

    public int AwayRubbersWon => _rubbers.Count(r => r.AwayScore > r.HomeScore);

    public Rubber AddRubber(Guid homePlayerId, Guid awayPlayerId, int homeScore, int awayScore)
    {
        var rubber = new Rubber(_rubbers.Count + 1, homePlayerId, awayPlayerId, homeScore, awayScore);
        _rubbers.Add(rubber);
        return rubber;
    }
}

public sealed class Rubber
{
    /// <summary>Used by EF Core when loading.</summary>
    private Rubber()
    {
    }

    internal Rubber(int number, Guid homePlayerId, Guid awayPlayerId, int homeScore, int awayScore)
    {
        Guard.That(homePlayerId != awayPlayerId, "A player cannot play themselves.");
        Number = number;
        HomePlayerId = Guard.NotEmpty(homePlayerId, "Home player");
        AwayPlayerId = Guard.NotEmpty(awayPlayerId, "Away player");
        HomeScore = Guard.NotNegative(homeScore, "Home score");
        AwayScore = Guard.NotNegative(awayScore, "Away score");
    }

    public int Number { get; private set; }

    public Guid HomePlayerId { get; private set; }

    public Guid AwayPlayerId { get; private set; }

    public int HomeScore { get; private set; }

    public int AwayScore { get; private set; }
}
