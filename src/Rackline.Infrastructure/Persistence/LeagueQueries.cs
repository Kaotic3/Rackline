using Microsoft.EntityFrameworkCore;
using Rackline.Application.Leagues;
using Rackline.Domain.Leagues;

namespace Rackline.Infrastructure.Persistence;

internal sealed class LeagueQueries(RacklineDbContext db) : ILeagueQueries
{
    public async Task<IReadOnlyList<LeagueSummaryDto>> ListAsync(CancellationToken cancellationToken) =>
        await Summaries(db.Leagues.OrderBy(l => l.Name)).ToListAsync(cancellationToken);

    public Task<LeagueSummaryDto?> GetAsync(Guid leagueId, CancellationToken cancellationToken) =>
        Summaries(db.Leagues.Where(l => l.Id == leagueId)).SingleOrDefaultAsync(cancellationToken);

    public async Task<SeasonDetailDto?> GetSeasonAsync(Guid seasonId, CancellationToken cancellationToken)
    {
        var row = await db.Leagues
            .AsNoTracking()
            .SelectMany(l => l.Seasons.Where(s => s.Id == seasonId), (l, s) => new
            {
                LeagueId = l.Id,
                LeagueName = l.Name,
                l.Discipline,
                Season = new
                {
                    s.Id,
                    s.Name,
                    s.StartsOn,
                    s.EndsOn,
                    s.Status,
                    Rules = new SeasonRulesDto(
                        s.Rules.RubbersPerFixture,
                        s.Rules.RubberLength.Kind,
                        s.Rules.RubberLength.Value,
                        s.Rules.PointsForWin,
                        s.Rules.PointsForDraw,
                        s.Rules.PointsPerRubber),
                },
                Teams = s.Teams.Select(t => new { t.Id, t.Name, t.HomeVenueId, t.PlayerIds }).ToList(),
                Fixtures = s.Fixtures
                    .OrderBy(f => f.Round).ThenBy(f => f.ScheduledFor)
                    .Select(f => new
                    {
                        f.Id,
                        f.Round,
                        f.HomeTeamId,
                        f.AwayTeamId,
                        f.ScheduledFor,
                        f.Status,
                        f.CompletedAt,
                        Rubbers = f.Rubbers.OrderBy(r => r.Number)
                            .Select(r => new RubberDto(r.Number, r.HomePlayerId, r.AwayPlayerId, r.HomeScore, r.AwayScore))
                            .ToList(),
                    })
                    .ToList(),
            })
            .AsSplitQuery()
            .SingleOrDefaultAsync(cancellationToken);

        if (row is null)
        {
            return null;
        }

        // One query each for every name the season mentions (including players who have since left a roster).
        var playerIds = row.Teams.SelectMany(t => t.PlayerIds)
            .Concat(row.Fixtures.SelectMany(f => f.Rubbers).SelectMany(r => new[] { r.HomePlayerId, r.AwayPlayerId }))
            .Distinct()
            .ToList();
        var players = await db.Players
            .AsNoTracking()
            .Where(p => playerIds.Contains(p.Id))
            .Select(p => new { p.Id, Name = p.FirstName + " " + p.LastName })
            .ToDictionaryAsync(p => p.Id, p => p.Name, cancellationToken);

        var venueIds = row.Teams.Select(t => t.HomeVenueId).OfType<Guid>().Distinct().ToList();
        var venues = await db.Venues
            .AsNoTracking()
            .Where(v => venueIds.Contains(v.Id))
            .ToDictionaryAsync(v => v.Id, v => v.Name, cancellationToken);

        var teams = row.Teams
            .OrderBy(t => t.Name)
            .Select(t => new TeamDto(
                t.Id,
                t.Name,
                t.HomeVenueId,
                t.HomeVenueId is { } v ? venues.GetValueOrDefault(v) : null,
                [.. t.PlayerIds.Select(id => new RosterPlayerDto(id, players.GetValueOrDefault(id, "Unknown player"))).OrderBy(p => p.Name)]))
            .ToList();

        // Players who have left a roster still appear in old rubbers.
        var rostered = teams.SelectMany(t => t.Players).Select(p => p.Id).ToHashSet();
        var former = players.Where(p => !rostered.Contains(p.Key)).Select(p => new RosterPlayerDto(p.Key, p.Value)).ToList();

        var season = row.Season;
        return new SeasonDetailDto(
            season.Id,
            row.LeagueId,
            row.LeagueName,
            row.Discipline,
            season.Name,
            season.StartsOn,
            season.EndsOn,
            season.Status,
            season.Rules,
            teams,
            [.. row.Fixtures.Select(f => new FixtureDto(f.Id, f.Round, f.HomeTeamId, f.AwayTeamId, f.ScheduledFor, f.Status, f.CompletedAt, f.Rubbers))])
        {
            FormerPlayers = former,
        };
    }

    /// <summary>Filter and sort before calling this: EF cannot translate either once rows are DTOs.</summary>
    private static IQueryable<LeagueSummaryDto> Summaries(IQueryable<League> leagues) =>
        leagues
            .AsNoTracking()
            .Select(l => new LeagueSummaryDto(
                l.Id,
                l.OrganisationId,
                l.Name,
                l.Discipline,
                l.Seasons
                    .OrderByDescending(s => s.StartsOn)
                    .Select(s => new SeasonSummaryDto(s.Id, s.Name, s.StartsOn, s.EndsOn, s.Status, s.Teams.Count))
                    .ToList()));
}
