using System.Globalization;
using Rackline.Domain.Common;
using Rackline.Domain.Leagues;
using Rackline.Domain.Organisations;
using Rackline.Domain.Tournaments;

namespace Rackline.Web.Components.Shared;

/// <summary>Display text for enums and dates, in British English.</summary>
public static class Labels
{
    private static readonly CultureInfo British = CultureInfo.GetCultureInfo("en-GB");

    public static string Of(Discipline discipline) => discipline switch
    {
        Discipline.EnglishEightBall => "English 8-ball",
        Discipline.UsEightBall => "US 8-ball",
        Discipline.NineBall => "9-ball",
        Discipline.TenBall => "10-ball",
        Discipline.StraightPool => "Straight pool",
        Discipline.Snooker => "Snooker",
        Discipline.Heyball => "Heyball",
        _ => discipline.ToString(),
    };

    public static string Of(TournamentFormat format) => format switch
    {
        TournamentFormat.SingleElimination => "Single elimination",
        TournamentFormat.DoubleElimination => "Double elimination",
        TournamentFormat.RoundRobin => "Round robin",
        TournamentFormat.GroupsThenKnockout => "Groups then knockout",
        TournamentFormat.Swiss => "Swiss",
        _ => format.ToString(),
    };

    public static string Of(TournamentStatus status) => status switch
    {
        TournamentStatus.Draft => "Draft",
        TournamentStatus.Registration => "Taking entries",
        TournamentStatus.Drawn => "Draw made",
        TournamentStatus.InProgress => "In progress",
        TournamentStatus.Completed => "Finished",
        TournamentStatus.Cancelled => "Cancelled",
        _ => status.ToString(),
    };

    public static string Of(SeasonStatus status) => status switch
    {
        SeasonStatus.Setup => "Being set up",
        SeasonStatus.Scheduled => "Under way",
        SeasonStatus.Completed => "Finished",
        _ => status.ToString(),
    };

    public static string Of(FixtureStatus status) => status switch
    {
        FixtureStatus.Scheduled => "Scheduled",
        FixtureStatus.Postponed => "Postponed",
        FixtureStatus.Completed => "Played",
        _ => status.ToString(),
    };

    public static string Of(OrganisationLevel level) => level switch
    {
        OrganisationLevel.International => "International body",
        OrganisationLevel.National => "National body",
        OrganisationLevel.Regional => "Region",
        OrganisationLevel.Club => "Club",
        _ => level.ToString(),
    };

    public static string Of(StaffRole role) => role switch
    {
        StaffRole.Organiser => "Organiser",
        StaffRole.Referee => "Referee",
        _ => role.ToString(),
    };

    public static string Date(DateOnly date) => date.ToString("d MMM yyyy", British);

    /// <summary>A fixture's date and UTC time, or a note that it is postponed with no new date.</summary>
    public static string When(DateTimeOffset? at) => at is { } value ? value.UtcDateTime.ToString("ddd d MMM yyyy, HH:mm 'UTC'", British) : "Date to be confirmed";

    /// <summary>Dates are stored in UTC and shown as a UTC calendar date.</summary>
    public static string Date(DateTimeOffset? at) => at is { } value ? value.UtcDateTime.ToString("d MMM yyyy", British) : "";

    public static string Rating(double value) => value.ToString("0", British);

    /// <summary>A rating change with its sign, e.g. "+12" or "−8".</summary>
    public static string RatingChange(double change)
    {
        var rounded = Math.Round(change, MidpointRounding.AwayFromZero);
        return rounded switch
        {
            > 0 => $"+{rounded.ToString("0", British)}",
            < 0 => $"−{(-rounded).ToString("0", British)}",
            _ => "0",
        };
    }

    /// <summary>1st, 2nd, 3rd, 4th … 11th, 12th, 13th … 21st.</summary>
    public static string Ordinal(int number)
    {
        var suffix = (number % 100) is 11 or 12 or 13 ? "th" : (number % 10) switch
        {
            1 => "st",
            2 => "nd",
            3 => "rd",
            _ => "th",
        };
        return $"{number}{suffix}";
    }

    /// <summary>"1 tournament", "3 tournaments".</summary>
    public static string Count(int count, string noun) => count == 1 ? $"1 {noun}" : $"{count} {noun}s";
}
