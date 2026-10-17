using Rackline.Application.Common;
using Rackline.Application.Players;
using Rackline.Application.Rankings;
using Rackline.Domain.Common;

namespace Rackline.Web.Api;

internal static class RankingEndpoints
{
    public const int HistoryLimit = 50;

    public static RouteGroupBuilder MapRankingEndpoints(this RouteGroupBuilder api)
    {
        var rankings = api.MapGroup("/rankings").WithTags("Rankings");

        rankings.MapGet("/", (IRankingQueries queries, CancellationToken ct) => queries.ListAsync(ct));

        rankings.MapGet("/{id:guid}", async (Guid id, IRankingQueries queries, CancellationToken ct) =>
            await queries.GetAsync(id, ct) ?? throw new NotFoundException("Ranking", id));

        rankings.MapPost("/", async (CreateRankingRequest request, RankingSetup setup, IRankingQueries queries, CancellationToken ct) =>
        {
            var id = await setup.CreateAsync(
                request.OrganisationId!.Value, request.Name, request.Discipline!.Value, request.Season,
                request.PointsByPosition, request.ParticipationPoints, ct);
            return Results.Created($"/api/rankings/{id}", await queries.GetAsync(id, ct));
        })
        .RequireAuthorization(Policies.ManageRankings);

        var ratings = api.MapGroup("/ratings").WithTags("Ratings");

        ratings.MapGet("/", (Discipline discipline, IRatingQueries queries, CancellationToken ct) => queries.LeaderboardAsync(discipline, ct));

        ratings.MapPost("/rebuild", async (RatingRebuild rebuild, CancellationToken ct) => new RebuildRatingsResponse(await rebuild.RebuildAsync(ct)))
            .RequireAuthorization(Policies.RebuildRatings);

        var player = api.MapGroup("/players/{id:guid}").WithTags("Players");

        player.MapGet("/ratings", async (Guid id, IPlayerQueries players, IRatingQueries queries, CancellationToken ct) =>
        {
            var profile = await players.GetProfileAsync(id, ct) ?? throw new NotFoundException("Player", id);
            return new PlayerRatingsResponse(profile.Ratings, await queries.HistoryAsync(id, HistoryLimit, ct));
        });

        player.MapGet("/rankings", async (Guid id, IReferenceData references, IRankingQueries queries, CancellationToken ct) =>
            await references.PlayerExistsAsync(id, ct)
                ? await queries.ForPlayerAsync(id, ct)
                : throw new NotFoundException("Player", id));

        return api;
    }
}
