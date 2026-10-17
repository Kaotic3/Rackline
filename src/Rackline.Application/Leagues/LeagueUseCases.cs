using Rackline.Application.Common;
using Rackline.Domain.Common;
using Rackline.Domain.Leagues;

namespace Rackline.Application.Leagues;

/// <summary>The settings an organiser chooses for a season.</summary>
public sealed record SeasonRulesInput(
    int RubbersPerFixture,
    MatchLengthKind RubberLengthKind,
    int RubberLengthValue,
    int PointsForWin = 2,
    int PointsForDraw = 1,
    int PointsPerRubber = 0)
{
    internal SeasonRules ToRules() => new(
        RubbersPerFixture,
        RubberLengthKind == MatchLengthKind.RaceTo ? MatchLength.RaceTo(RubberLengthValue) : MatchLength.BestOf(RubberLengthValue),
        PointsForWin,
        PointsForDraw,
        PointsPerRubber);
}

/// <summary>Creating leagues and seasons, entering teams and players, and making the fixtures.</summary>
public sealed class LeagueSetup(ILeagueRepository leagues, IReferenceData references)
{
    public async Task<Guid> CreateLeagueAsync(Guid organisationId, string name, Discipline discipline, CancellationToken cancellationToken = default)
    {
        if (!await references.OrganisationExistsAsync(organisationId, cancellationToken))
        {
            throw new NotFoundException("Organisation", organisationId);
        }

        var league = new League(organisationId, name, discipline);
        leagues.Add(league);
        await leagues.SaveChangesAsync(cancellationToken);
        return league.Id;
    }

    public async Task<Guid> AddSeasonAsync(Guid leagueId, string name, DateOnly startsOn, DateOnly endsOn, SeasonRulesInput rules, CancellationToken cancellationToken = default)
    {
        var league = await leagues.FindAsync(leagueId, cancellationToken) ?? throw new NotFoundException("League", leagueId);
        var season = league.AddSeason(name, startsOn, endsOn, rules.ToRules());
        await leagues.SaveChangesAsync(cancellationToken);
        return season.Id;
    }

    public async Task<Guid> AddTeamAsync(Guid seasonId, string name, Guid? homeVenueId, CancellationToken cancellationToken = default)
    {
        if (homeVenueId is { } venue && !await references.VenueExistsAsync(venue, cancellationToken))
        {
            throw new NotFoundException("Venue", venue);
        }

        var season = await leagues.GetSeasonAsync(seasonId, cancellationToken);
        var team = season.AddTeam(name, homeVenueId);
        await leagues.SaveChangesAsync(cancellationToken);
        return team.Id;
    }

    public Task RemoveTeamAsync(Guid seasonId, Guid teamId, CancellationToken cancellationToken = default) =>
        ChangeSeasonAsync(seasonId, s => s.RemoveTeam(s.RequireTeam(teamId)), cancellationToken);

    public async Task AddPlayerAsync(Guid seasonId, Guid teamId, Guid playerId, CancellationToken cancellationToken = default)
    {
        if (!await references.PlayerExistsAsync(playerId, cancellationToken))
        {
            throw new NotFoundException("Player", playerId);
        }

        await ChangeSeasonAsync(seasonId, s => s.AddPlayerToTeam(s.RequireTeam(teamId), playerId), cancellationToken);
    }

    public Task RemovePlayerAsync(Guid seasonId, Guid teamId, Guid playerId, CancellationToken cancellationToken = default) =>
        ChangeSeasonAsync(seasonId, s => s.RemovePlayerFromTeam(s.RequireTeam(teamId), playerId), cancellationToken);

    public Task GenerateFixturesAsync(Guid seasonId, DateTimeOffset firstRound, int daysBetweenRounds, bool homeAndAway, CancellationToken cancellationToken = default) =>
        ChangeSeasonAsync(seasonId, s => s.GenerateFixtures(firstRound, daysBetweenRounds, homeAndAway), cancellationToken);

    public Task CompleteSeasonAsync(Guid seasonId, CancellationToken cancellationToken = default) =>
        ChangeSeasonAsync(seasonId, s => s.Complete(), cancellationToken);

    private async Task ChangeSeasonAsync(Guid seasonId, Action<Season> change, CancellationToken cancellationToken)
    {
        change(await leagues.GetSeasonAsync(seasonId, cancellationToken));
        await leagues.SaveChangesAsync(cancellationToken);
    }
}

/// <summary>Entering, correcting and rescheduling fixtures.</summary>
public sealed class LeagueResults(ILeagueRepository leagues, TimeProvider time)
{
    public async Task RecordResultAsync(Guid seasonId, Guid fixtureId, IReadOnlyList<RubberResult> rubbers, CancellationToken cancellationToken = default)
    {
        var season = await leagues.GetSeasonAsync(seasonId, cancellationToken);
        season.RecordResult(season.RequireFixture(fixtureId), rubbers, time.GetUtcNow());
        await leagues.SaveChangesAsync(cancellationToken);
    }

    /// <summary>Moves a fixture; a null date postpones it until a new one is agreed.</summary>
    public async Task RescheduleAsync(Guid seasonId, Guid fixtureId, DateTimeOffset? scheduledFor, CancellationToken cancellationToken = default)
    {
        var season = await leagues.GetSeasonAsync(seasonId, cancellationToken);
        season.Reschedule(season.RequireFixture(fixtureId), scheduledFor);
        await leagues.SaveChangesAsync(cancellationToken);
    }
}

internal static class LeagueLookups
{
    public static async Task<Season> GetSeasonAsync(this ILeagueRepository leagues, Guid seasonId, CancellationToken cancellationToken)
    {
        var league = await leagues.FindBySeasonAsync(seasonId, cancellationToken) ?? throw new NotFoundException("Season", seasonId);
        return league.FindSeason(seasonId);
    }

    /// <summary>The id back again if the season has that team; otherwise a 404-style error rather than a rule breach.</summary>
    public static Guid RequireTeam(this Season season, Guid teamId) =>
        season.Teams.Any(t => t.Id == teamId) ? teamId : throw new NotFoundException("Team", teamId);

    public static Guid RequireFixture(this Season season, Guid fixtureId) =>
        season.Fixtures.Any(f => f.Id == fixtureId) ? fixtureId : throw new NotFoundException("Fixture", fixtureId);
}
