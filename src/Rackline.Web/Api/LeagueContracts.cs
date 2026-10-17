using System.ComponentModel.DataAnnotations;
using Rackline.Application.Leagues;
using Rackline.Domain.Common;
using Rackline.Domain.Leagues;

namespace Rackline.Web.Api;

public sealed record CreateLeagueRequest(
    [Required] Guid? OrganisationId,
    [Required, StringLength(200, MinimumLength = 1)] string Name,
    [Required] Discipline? Discipline);

public sealed record CreateSeasonRequest(
    [Required, StringLength(100, MinimumLength = 1)] string Name,
    [Required] DateOnly? StartsOn,
    [Required] DateOnly? EndsOn,
    [Range(1, 30)] int RubbersPerFixture,
    [Required] MatchLengthKind? RubberLengthKind,
    [Range(1, 99)] int RubberLengthValue,
    [Range(0, 100)] int PointsForWin = 2,
    [Range(0, 100)] int PointsForDraw = 1,
    [Range(0, 100)] int PointsPerRubber = 0) : IValidatableObject
{
    public SeasonRulesInput ToRules() =>
        new(RubbersPerFixture, RubberLengthKind!.Value, RubberLengthValue, PointsForWin, PointsForDraw, PointsPerRubber);

    public IEnumerable<ValidationResult> Validate(ValidationContext validationContext)
    {
        foreach (var problem in RequestRules.MatchLength(RubberLengthKind, RubberLengthValue))
        {
            yield return problem;
        }

        if (StartsOn > EndsOn)
        {
            yield return new ValidationResult("A season cannot end before it starts.", [nameof(EndsOn)]);
        }

        if (PointsForDraw > PointsForWin)
        {
            yield return new ValidationResult("A win must be worth at least as much as a draw.", [nameof(PointsForWin)]);
        }
    }
}

public sealed record AddTeamRequest(
    [Required, StringLength(100, MinimumLength = 1)] string Name,
    Guid? HomeVenueId);

public sealed record GenerateFixturesRequest(
    [Required] DateTimeOffset? FirstRound,
    [Range(1, 60)] int DaysBetweenRounds = 7,
    bool HomeAndAway = true);

public sealed record RubberInput(
    [Required] Guid? HomePlayerId,
    [Required] Guid? AwayPlayerId,
    [Range(0, 999)] int HomeScore,
    [Range(0, 999)] int AwayScore);

public sealed record RecordFixtureResultRequest([Required, MinLength(1), MaxLength(30)] RubberInput[] Rubbers)
{
    public IReadOnlyList<RubberResult> ToRubbers() =>
        [.. Rubbers.Select(r => new RubberResult(r.HomePlayerId!.Value, r.AwayPlayerId!.Value, r.HomeScore, r.AwayScore))];
}

/// <summary>A null date postpones the fixture until a new one is agreed.</summary>
public sealed record RescheduleFixtureRequest(DateTimeOffset? ScheduledFor);

public sealed record SeasonResponse(SeasonDetailDto Season, IReadOnlyList<LeagueTableRowDto> Table);
