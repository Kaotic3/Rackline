using Rackline.Application.Common;
using Rackline.Application.Tournaments;
using Rackline.Domain.Tournaments;

namespace Rackline.Web.Api;

internal static class TournamentEndpoints
{
    public static RouteGroupBuilder MapTournamentEndpoints(this RouteGroupBuilder api)
    {
        var tournaments = api.MapGroup("/tournaments").WithTags("Tournaments");

        // Reading is public.
        tournaments.MapGet("/", (TournamentStatus? status, ITournamentQueries queries, CancellationToken ct) =>
            queries.ListAsync(status, ct));

        tournaments.MapGet("/{id:guid}", (Guid id, ITournamentQueries queries, CancellationToken ct) =>
            DetailAsync(id, queries, ct));

        var manage = tournaments.MapGroup("").RequireAuthorization(Policies.ManageTournaments);

        manage.MapPost("/", async (CreateTournamentRequest request, TournamentLifecycle lifecycle, ITournamentQueries queries, CancellationToken ct) =>
        {
            var id = await lifecycle.CreateAsync(request.OrganisationId!.Value, request.ToSettings(), ct);
            return Results.Created($"/api/tournaments/{id}", await queries.GetAsync(id, ct));
        });

        manage.MapPut("/{id:guid}", async (Guid id, UpdateTournamentRequest request, TournamentLifecycle lifecycle, ITournamentQueries queries, CancellationToken ct) =>
        {
            await lifecycle.UpdateAsync(id, request.ToSettings(), ct);
            return await DetailAsync(id, queries, ct);
        });

        manage.MapDelete("/{id:guid}", async (Guid id, TournamentLifecycle lifecycle, CancellationToken ct) =>
        {
            await lifecycle.DeleteAsync(id, ct);
            return Results.NoContent();
        });

        MapTransition(manage, "/{id:guid}/open-registration", (l, id, ct) => l.OpenRegistrationAsync(id, ct));
        MapTransition(manage, "/{id:guid}/start", (l, id, ct) => l.StartAsync(id, ct));
        MapTransition(manage, "/{id:guid}/complete", (l, id, ct) => l.CompleteAsync(id, ct));
        MapTransition(manage, "/{id:guid}/cancel", (l, id, ct) => l.CancelAsync(id, ct));

        // Entries and seeding.
        manage.MapPost("/{id:guid}/entries", async (Guid id, AddEntryRequest request, TournamentEntries entries, ITournamentQueries queries, CancellationToken ct) =>
        {
            await entries.AddAsync(id, request.PlayerId!.Value, request.Seed, request.Handicap, ct);
            return Results.Created($"/api/tournaments/{id}", await queries.GetAsync(id, ct));
        });

        manage.MapPut("/{id:guid}/entries/{entryId:guid}", async (Guid id, Guid entryId, UpdateEntryRequest request, TournamentEntries entries, ITournamentQueries queries, CancellationToken ct) =>
        {
            await entries.UpdateAsync(id, entryId, request.Seed, request.Handicap, ct);
            return await DetailAsync(id, queries, ct);
        });

        manage.MapDelete("/{id:guid}/entries/{entryId:guid}", async (Guid id, Guid entryId, TournamentEntries entries, ITournamentQueries queries, CancellationToken ct) =>
        {
            await entries.WithdrawAsync(id, entryId, ct);
            return await DetailAsync(id, queries, ct);
        });

        // The draw.
        manage.MapPost("/{id:guid}/draw", async (Guid id, int? randomSeed, TournamentDraws draws, ITournamentQueries queries, CancellationToken ct) =>
        {
            var seed = await draws.MakeAsync(id, randomSeed, ct);
            return Results.Ok(new DrawResponse(seed, (await queries.GetAsync(id, ct))!));
        });

        manage.MapDelete("/{id:guid}/draw", async (Guid id, TournamentDraws draws, ITournamentQueries queries, CancellationToken ct) =>
        {
            await draws.UndoAsync(id, ct);
            return await DetailAsync(id, queries, ct);
        });

        // Matches: referees can run these as well as organisers.
        var score = tournaments.MapGroup("/{id:guid}/matches/{number:int}").RequireAuthorization(Policies.ScoreMatches);

        score.MapPost("/start", async (Guid id, int number, Guid? tableId, MatchResults results, ITournamentQueries queries, CancellationToken ct) =>
        {
            await results.StartAsync(id, number, tableId, ct);
            return await DetailAsync(id, queries, ct);
        });

        score.MapPost("/result", async (Guid id, int number, RecordResultRequest request, MatchResults results, ITournamentQueries queries, CancellationToken ct) =>
        {
            await results.RecordResultAsync(id, number, request.Score1!.Value, request.Score2!.Value, ct);
            return await DetailAsync(id, queries, ct);
        });

        score.MapPost("/walkover", async (Guid id, int number, AwardWalkoverRequest request, MatchResults results, ITournamentQueries queries, CancellationToken ct) =>
        {
            await results.AwardWalkoverAsync(id, number, request.WinnerEntryId!.Value, ct);
            return await DetailAsync(id, queries, ct);
        });

        return api;
    }

    private static void MapTransition(RouteGroupBuilder group, string pattern, Func<TournamentLifecycle, Guid, CancellationToken, Task> transition) =>
        group.MapPost(pattern, async (Guid id, TournamentLifecycle lifecycle, ITournamentQueries queries, CancellationToken ct) =>
        {
            await transition(lifecycle, id, ct);
            return await DetailAsync(id, queries, ct);
        });

    private static async Task<IResult> DetailAsync(Guid id, ITournamentQueries queries, CancellationToken ct) =>
        await queries.GetAsync(id, ct) is { } detail
            ? Results.Ok(detail)
            : throw new NotFoundException("Tournament", id);
}
