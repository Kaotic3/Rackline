using System.ComponentModel.DataAnnotations;
using Rackline.Application.Players;
using Rackline.Application.Rankings;
using Rackline.Domain.Common;
using Rackline.Domain.Rankings;

namespace Rackline.Web.Api;

public sealed record CreateRankingRequest(
    [Required] Guid? OrganisationId,
    [Required, StringLength(200, MinimumLength = 1)] string Name,
    [Required] Discipline? Discipline,
    [Required, StringLength(50, MinimumLength = 1)] string Season,
    [Required, MinLength(1), MaxLength(Ranking.MaxPositions)] int[] PointsByPosition,
    [Range(0, 100_000)] int ParticipationPoints = 0) : IValidatableObject
{
    public IEnumerable<ValidationResult> Validate(ValidationContext validationContext)
    {
        if (PointsByPosition is not { Length: > 0 } points)
        {
            yield break;
        }

        if (points.Any(p => p is < 0 or > 100_000))
        {
            yield return new ValidationResult("Points must be between 0 and 100,000.", [nameof(PointsByPosition)]);
        }

        if (points.Zip(points.Skip(1)).Any(pair => pair.First < pair.Second) || ParticipationPoints > points[^1])
        {
            yield return new ValidationResult("A higher position must never be worth fewer points than a lower one.", [nameof(PointsByPosition)]);
        }
    }
}

public sealed record PlayerRatingsResponse(IReadOnlyList<RatingDto> Ratings, IReadOnlyList<RatingChangeDto> History);

public sealed record RebuildRatingsResponse(int ResultsRated);
