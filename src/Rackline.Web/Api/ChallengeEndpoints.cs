using System.ComponentModel.DataAnnotations;
using Rackline.Application.Challenges;
using Rackline.Application.Common;
using Rackline.Domain.Common;

namespace Rackline.Web.Api;

public sealed record RecordChallengeRequest(
    [Required] Guid? OrganisationId,
    [Required] Guid? Player1Id,
    [Required] Guid? Player2Id,
    [Required] Discipline? Discipline,
    [Required] MatchLengthKind? MatchLengthKind,
    [Range(1, 99)] int MatchLengthValue,
    [Required, Range(0, 999)] int? Score1,
    [Required, Range(0, 999)] int? Score2) : IValidatableObject
{
    public IEnumerable<ValidationResult> Validate(ValidationContext validationContext) =>
        RequestRules.MatchLength(MatchLengthKind, MatchLengthValue);
}

internal static class ChallengeEndpoints
{
    public const int DefaultLimit = 50;
    public const int MaxLimit = 200;

    public static RouteGroupBuilder MapChallengeEndpoints(this RouteGroupBuilder api)
    {
        var challenges = api.MapGroup("/challenges").WithTags("Challenges");

        challenges.MapGet("/", (Guid? playerId, int? limit, IChallengeQueries queries, CancellationToken ct) =>
            queries.ListAsync(playerId, Math.Clamp(limit ?? DefaultLimit, 1, MaxLimit), ct))
            .WithSummary("Challenges, newest first; only one player's with ?playerId=. At most 200 (?limit=).");

        challenges.MapGet("/{id:guid}", async (Guid id, IChallengeQueries queries, CancellationToken ct) =>
            await queries.GetAsync(id, ct) ?? throw new NotFoundException("Challenge", id))
            .WithSummary("One challenge: the result, whether it counted towards ratings, and if not, why not.");

        // Anyone may ask whether a challenge would count: ratings are public anyway.
        challenges.MapGet("/preview", async (Guid player1, Guid player2, Discipline discipline, ChallengeRecording recording, CancellationToken ct) =>
        {
            var assessment = await recording.PreviewAsync(player1, player2, discipline, ct);
            return new ChallengePreviewDto(assessment.Counts, assessment.Reasons);
        })
        .WithSummary("Whether a challenge between two players would count towards ratings if recorded now.");

        challenges.MapPost("/", async (RecordChallengeRequest request, ChallengeRecording recording, IChallengeQueries queries, CancellationToken ct) =>
        {
            var id = await recording.RecordAsync(
                request.OrganisationId!.Value, request.Player1Id!.Value, request.Player2Id!.Value, request.Discipline!.Value,
                request.MatchLengthKind!.Value, request.MatchLengthValue, request.Score1!.Value, request.Score2!.Value, ct);
            return Results.Created($"/api/challenges/{id}", await queries.GetAsync(id, ct));
        })
        .RequireAuthorization(Policies.ScoreMatches);

        challenges.MapDelete("/{id:guid}", async (Guid id, ChallengeRecording recording, CancellationToken ct) =>
        {
            await recording.DeleteAsync(id, ct);
            return Results.NoContent();
        })
        .RequireAuthorization(Policies.ManageOrganisations);

        return api;
    }
}
