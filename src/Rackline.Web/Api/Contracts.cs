using System.ComponentModel.DataAnnotations;
using Rackline.Application.Tournaments;
using Rackline.Domain.Common;
using Rackline.Domain.Tournaments;

namespace Rackline.Web.Api;

public sealed record CreateTournamentRequest(
    [Required] Guid? OrganisationId,
    [Required, StringLength(200, MinimumLength = 1)] string Name,
    [Required] Discipline? Discipline,
    [Required] TournamentFormat? Format,
    [Required] MatchLengthKind? MatchLengthKind,
    [Range(1, 99)] int MatchLengthValue,
    bool GrandFinalReset = true,
    [Range(1, 32)] int GroupCount = 1,
    Guid? RankingId = null) : IValidatableObject
{
    public TournamentSettings ToSettings() =>
        new(Name, Discipline!.Value, Format!.Value, MatchLengthKind!.Value, MatchLengthValue, GrandFinalReset, GroupCount, RankingId);

    public IEnumerable<ValidationResult> Validate(ValidationContext validationContext) =>
        RequestRules.MatchLength(MatchLengthKind, MatchLengthValue);
}

public sealed record UpdateTournamentRequest(
    [Required, StringLength(200, MinimumLength = 1)] string Name,
    [Required] Discipline? Discipline,
    [Required] TournamentFormat? Format,
    [Required] MatchLengthKind? MatchLengthKind,
    [Range(1, 99)] int MatchLengthValue,
    bool GrandFinalReset = true,
    [Range(1, 32)] int GroupCount = 1,
    Guid? RankingId = null) : IValidatableObject
{
    public TournamentSettings ToSettings() =>
        new(Name, Discipline!.Value, Format!.Value, MatchLengthKind!.Value, MatchLengthValue, GrandFinalReset, GroupCount, RankingId);

    public IEnumerable<ValidationResult> Validate(ValidationContext validationContext) =>
        RequestRules.MatchLength(MatchLengthKind, MatchLengthValue);
}

public sealed record AddEntryRequest(
    [Required] Guid? PlayerId,
    [Range(1, 1024)] int? Seed,
    [Range(-100, 100)] int Handicap = 0);

public sealed record UpdateEntryRequest(
    [Range(1, 1024)] int? Seed,
    [Range(-100, 100)] int Handicap = 0);

public sealed record RecordResultRequest(
    [Required, Range(0, 999)] int? Score1,
    [Required, Range(0, 999)] int? Score2);

public sealed record AwardWalkoverRequest([Required] Guid? WinnerEntryId);

public sealed record RecordFrameRequest(
    [Required, Range(1, 2)] int? WinnerSlot,
    bool BreakAndRun = false,
    [Range(0, 999)] int? Points1 = null,
    [Range(0, 999)] int? Points2 = null);

public sealed record SetRolesRequest([Required] string[] Roles);

public sealed record DrawResponse(int RandomSeed, TournamentDetailDto Tournament);

public sealed record UserDto(Guid Id, string? Email, IReadOnlyList<string> Roles);

internal static class RequestRules
{
    public static IEnumerable<ValidationResult> MatchLength(MatchLengthKind? kind, int value)
    {
        if (kind == Domain.Common.MatchLengthKind.BestOf && value % 2 == 0)
        {
            yield return new ValidationResult("Best-of must be an odd number of frames.", [nameof(MatchLengthKind)]);
        }
    }
}
