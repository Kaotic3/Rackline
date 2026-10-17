using Rackline.Application.Common;
using Rackline.Application.Leagues;

namespace Rackline.Web.Api;

internal static class LeagueEndpoints
{
    public static RouteGroupBuilder MapLeagueEndpoints(this RouteGroupBuilder api)
    {
        var leagues = api.MapGroup("/leagues").WithTags("Leagues");

        leagues.MapGet("/", (ILeagueQueries queries, CancellationToken ct) => queries.ListAsync(ct))
            .WithSummary("Leagues, with their seasons.");

        leagues.MapGet("/{id:guid}", async (Guid id, ILeagueQueries queries, CancellationToken ct) =>
            await queries.GetAsync(id, ct) ?? throw new NotFoundException("League", id))
            .WithSummary("One league, with its seasons.");

        var manageLeagues = leagues.MapGroup("").RequireAuthorization(Policies.ManageLeagues);

        manageLeagues.MapPost("/", async (CreateLeagueRequest request, LeagueSetup setup, ILeagueQueries queries, CancellationToken ct) =>
        {
            var id = await setup.CreateLeagueAsync(request.OrganisationId!.Value, request.Name, request.Discipline!.Value, ct);
            return Results.Created($"/api/leagues/{id}", await queries.GetAsync(id, ct));
        });

        manageLeagues.MapPost("/{id:guid}/seasons", async (Guid id, CreateSeasonRequest request, LeagueSetup setup, ILeagueQueries queries, CancellationToken ct) =>
        {
            var seasonId = await setup.AddSeasonAsync(id, request.Name, request.StartsOn!.Value, request.EndsOn!.Value, request.ToRules(), ct);
            return Results.Created($"/api/seasons/{seasonId}", await SeasonAsync(seasonId, queries, ct));
        });

        var seasons = api.MapGroup("/seasons/{id:guid}").WithTags("Leagues");

        seasons.MapGet("/", (Guid id, ILeagueQueries queries, CancellationToken ct) => SeasonAsync(id, queries, ct))
            .WithSummary("A season: rules, teams and rosters, fixtures and rubbers, and the league table.");

        var manage = seasons.MapGroup("").RequireAuthorization(Policies.ManageLeagues);

        manage.MapPost("/teams", async (Guid id, AddTeamRequest request, LeagueSetup setup, ILeagueQueries queries, CancellationToken ct) =>
        {
            await setup.AddTeamAsync(id, request.Name, request.HomeVenueId, ct);
            return Results.Created($"/api/seasons/{id}", await SeasonAsync(id, queries, ct));
        });

        manage.MapDelete("/teams/{teamId:guid}", (Guid id, Guid teamId, LeagueSetup setup, ILeagueQueries queries, CancellationToken ct) =>
            ThenSeasonAsync(id, queries, setup.RemoveTeamAsync(id, teamId, ct), ct));

        manage.MapPut("/teams/{teamId:guid}/players/{playerId:guid}", (Guid id, Guid teamId, Guid playerId, LeagueSetup setup, ILeagueQueries queries, CancellationToken ct) =>
            ThenSeasonAsync(id, queries, setup.AddPlayerAsync(id, teamId, playerId, ct), ct));

        manage.MapDelete("/teams/{teamId:guid}/players/{playerId:guid}", (Guid id, Guid teamId, Guid playerId, LeagueSetup setup, ILeagueQueries queries, CancellationToken ct) =>
            ThenSeasonAsync(id, queries, setup.RemovePlayerAsync(id, teamId, playerId, ct), ct));

        manage.MapPost("/fixtures", (Guid id, GenerateFixturesRequest request, LeagueSetup setup, ILeagueQueries queries, CancellationToken ct) =>
            ThenSeasonAsync(id, queries, setup.GenerateFixturesAsync(id, request.FirstRound!.Value.ToUniversalTime(), request.DaysBetweenRounds, request.HomeAndAway, ct), ct));

        manage.MapPut("/fixtures/{fixtureId:guid}/result", (Guid id, Guid fixtureId, RecordFixtureResultRequest request, LeagueResults results, ILeagueQueries queries, CancellationToken ct) =>
            ThenSeasonAsync(id, queries, results.RecordResultAsync(id, fixtureId, request.ToRubbers(), ct), ct));

        manage.MapPost("/fixtures/{fixtureId:guid}/reschedule", (Guid id, Guid fixtureId, RescheduleFixtureRequest request, LeagueResults results, ILeagueQueries queries, CancellationToken ct) =>
            ThenSeasonAsync(id, queries, results.RescheduleAsync(id, fixtureId, request.ScheduledFor?.ToUniversalTime(), ct), ct));

        manage.MapPost("/complete", (Guid id, LeagueSetup setup, ILeagueQueries queries, CancellationToken ct) =>
            ThenSeasonAsync(id, queries, setup.CompleteSeasonAsync(id, ct), ct));

        return api;
    }

    private static async Task<SeasonResponse> ThenSeasonAsync(Guid seasonId, ILeagueQueries queries, Task change, CancellationToken ct)
    {
        await change;
        return await SeasonAsync(seasonId, queries, ct);
    }

    private static async Task<SeasonResponse> SeasonAsync(Guid seasonId, ILeagueQueries queries, CancellationToken ct)
    {
        var season = await queries.GetSeasonAsync(seasonId, ct) ?? throw new NotFoundException("Season", seasonId);
        return new SeasonResponse(season, SeasonTable.For(season));
    }
}
