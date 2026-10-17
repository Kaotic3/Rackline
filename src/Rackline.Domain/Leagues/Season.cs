using Rackline.Domain.Common;

namespace Rackline.Domain.Leagues;

public enum SeasonStatus
{
    /// <summary>Teams and rosters are being set up.</summary>
    Setup,

    /// <summary>Fixtures exist; results are being entered.</summary>
    Scheduled,

    /// <summary>Every fixture has been played and the table is final.</summary>
    Completed,
}

public sealed class Season : Entity
{
    private readonly List<Team> _teams = [];
    private readonly List<Fixture> _fixtures = [];

    /// <summary>Used by EF Core when loading.</summary>
    private Season()
    {
        Name = null!;
        Rules = null!;
    }

    internal Season(Guid leagueId, string name, DateOnly startsOn, DateOnly endsOn, SeasonRules rules)
    {
        Guard.That(endsOn >= startsOn, "A season cannot end before it starts.");
        LeagueId = leagueId;
        Name = Guard.NotBlank(name, "Season name");
        StartsOn = startsOn;
        EndsOn = endsOn;
        Rules = rules;
    }

    public Guid LeagueId { get; private set; }

    public string Name { get; private set; }

    public DateOnly StartsOn { get; private set; }

    public DateOnly EndsOn { get; private set; }

    public SeasonRules Rules { get; private set; }

    public SeasonStatus Status { get; private set; } = SeasonStatus.Setup;

    public IReadOnlyList<Team> Teams => _teams;

    public IReadOnlyList<Fixture> Fixtures => _fixtures;

    public void ChangeRules(SeasonRules rules)
    {
        RequireStatus(SeasonStatus.Setup);
        Rules = rules;
    }

    public Team AddTeam(string name, Guid? homeVenueId = null)
    {
        RequireStatus(SeasonStatus.Setup);
        name = Guard.NotBlank(name, "Team name");
        Guard.That(
            _teams.All(t => !string.Equals(t.Name, name, StringComparison.OrdinalIgnoreCase)),
            $"A team called '{name}' is already in this season.");
        var team = new Team(Id, name, homeVenueId);
        _teams.Add(team);
        return team;
    }

    public void RemoveTeam(Guid teamId)
    {
        RequireStatus(SeasonStatus.Setup);
        _teams.Remove(FindTeam(teamId));
    }

    /// <summary>Registers a player with a team. A player may play for only one team in a season.</summary>
    public void AddPlayerToTeam(Guid teamId, Guid playerId)
    {
        Guard.NotEmpty(playerId, "Player");
        Guard.That(Status != SeasonStatus.Completed, "The season is over.");
        var team = FindTeam(teamId);
        Guard.That(
            _teams.All(t => t == team || !t.HasPlayer(playerId)),
            "That player is already registered with another team this season.");
        team.AddPlayer(playerId);
    }

    public void RemovePlayerFromTeam(Guid teamId, Guid playerId)
    {
        Guard.That(Status != SeasonStatus.Completed, "The season is over.");
        FindTeam(teamId).RemovePlayer(playerId);
    }

    /// <summary>
    /// Creates the whole season's fixtures, one round every <paramref name="daysBetweenRounds"/> days
    /// from <paramref name="firstRound"/>. After this, teams are fixed.
    /// </summary>
    public IReadOnlyList<Fixture> GenerateFixtures(DateTimeOffset firstRound, int daysBetweenRounds = 7, bool homeAndAway = true)
    {
        RequireStatus(SeasonStatus.Setup);
        Guard.That(_teams.Count >= 2, "A season needs at least two teams.");
        Guard.Positive(daysBetweenRounds, "Days between rounds");

        var pairings = LeagueSchedule.Generate([.. _teams.Select(t => t.Id)], homeAndAway);
        var lastRound = pairings.Max(p => p.Round);
        Guard.That(
            DateOnly.FromDateTime(firstRound.UtcDateTime) >= StartsOn
            && DateOnly.FromDateTime(firstRound.AddDays((lastRound - 1) * daysBetweenRounds).UtcDateTime) <= EndsOn,
            "The fixtures would fall outside the season's dates.");

        foreach (var p in pairings)
        {
            _fixtures.Add(new Fixture(Id, p.Round, p.HomeTeamId, p.AwayTeamId, firstRound.AddDays((p.Round - 1) * daysBetweenRounds)));
        }

        Status = SeasonStatus.Scheduled;
        return _fixtures;
    }

    /// <summary>
    /// Records (or corrects) a fixture's result: every rubber at once, each between registered
    /// players of the two teams and complete under the season's rubber length.
    /// </summary>
    public void RecordResult(Guid fixtureId, IReadOnlyList<RubberResult> rubbers, DateTimeOffset at)
    {
        RequireStatus(SeasonStatus.Scheduled);
        var fixture = FindFixture(fixtureId);
        Guard.That(rubbers.Count == Rules.RubbersPerFixture, $"A fixture has exactly {Rules.RubbersPerFixture} rubbers.");

        var home = FindTeam(fixture.HomeTeamId);
        var away = FindTeam(fixture.AwayTeamId);
        var target = Rules.RubberLength.FramesToWin;

        var accepted = rubbers.Select((r, i) =>
        {
            Guard.That(home.HasPlayer(r.HomePlayerId), $"Rubber {i + 1}: the home player is not registered with {home.Name}.");
            Guard.That(away.HasPlayer(r.AwayPlayerId), $"Rubber {i + 1}: the away player is not registered with {away.Name}.");
            Guard.That(
                r.HomeScore >= 0 && r.AwayScore >= 0 && Math.Max(r.HomeScore, r.AwayScore) == target && Math.Min(r.HomeScore, r.AwayScore) < target,
                $"Rubber {i + 1}: exactly one player must reach {target}.");
            return new Rubber(i + 1, r.HomePlayerId, r.AwayPlayerId, r.HomeScore, r.AwayScore);
        }).ToList();

        fixture.SetResult(accepted, at);
    }

    /// <summary>Moves a fixture to a new date, or postpones it with no date when <paramref name="scheduledFor"/> is null.</summary>
    public void Reschedule(Guid fixtureId, DateTimeOffset? scheduledFor)
    {
        RequireStatus(SeasonStatus.Scheduled);
        FindFixture(fixtureId).Reschedule(scheduledFor);
    }

    public void Complete()
    {
        RequireStatus(SeasonStatus.Scheduled);
        Guard.That(_fixtures.All(f => f.Status == FixtureStatus.Completed), "Every fixture must be played before the season is completed.");
        Status = SeasonStatus.Completed;
    }

    /// <summary>The league table from the results so far.</summary>
    public IReadOnlyList<LeagueTableRow> Table() =>
        LeagueTable.Calculate(
            [.. _teams.OrderBy(t => t.Name, StringComparer.OrdinalIgnoreCase).Select(t => t.Id)],
            _fixtures.Where(f => f.Status == FixtureStatus.Completed).Select(FixtureOutcome.From),
            Rules);

    public Team FindTeam(Guid teamId) =>
        _teams.SingleOrDefault(t => t.Id == teamId) ?? throw new DomainException("Team not found.");

    public Fixture FindFixture(Guid fixtureId) =>
        _fixtures.SingleOrDefault(f => f.Id == fixtureId) ?? throw new DomainException("Fixture not found.");

    private void RequireStatus(SeasonStatus status) =>
        Guard.That(Status == status, $"Not allowed while the season is {Status}.");
}
