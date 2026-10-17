using Rackline.Application.Tournaments;
using Rackline.Domain.Tournaments;

namespace Rackline.Application.Live;

/// <summary>Names shared by the server and every live client.</summary>
public static class LiveChannels
{
    public static string TournamentGroup(Guid tournamentId) => $"tournament:{tournamentId}";

    public static string MatchGroup(Guid matchId) => $"match:{matchId}";

    /// <summary>Sent to a match group with the match's latest <see cref="MatchDto"/>.</summary>
    public const string MatchUpdated = nameof(MatchUpdated);

    /// <summary>Sent to a tournament group with the tournament id and every match that changed.</summary>
    public const string MatchesUpdated = nameof(MatchesUpdated);

    /// <summary>Sent to a tournament group with the tournament id and its status after any other change.</summary>
    public const string TournamentUpdated = nameof(TournamentUpdated);
}

/// <summary>Tells live viewers what changed. Called only after changes are saved.</summary>
public interface ILiveNotifier
{
    Task MatchesChangedAsync(Guid tournamentId, IReadOnlyList<MatchDto> matches, CancellationToken cancellationToken);

    Task TournamentChangedAsync(Guid tournamentId, TournamentStatus status, CancellationToken cancellationToken);
}

/// <summary>A change, as seen by in-process subscribers such as interactive Blazor pages.</summary>
public sealed record LiveUpdate(Guid TournamentId, TournamentStatus? Status, IReadOnlyList<MatchDto> Matches);

/// <summary>An in-process copy of every live notification, for server-side subscribers.</summary>
public sealed class LiveFeed
{
    public event Action<LiveUpdate>? Published;

    public void Publish(LiveUpdate update) => Published?.Invoke(update);
}

/// <summary>Snapshots a tournament's matches so only the ones a command changed are broadcast.</summary>
internal sealed class MatchChanges
{
    private readonly Dictionary<Guid, MatchDto> _before;

    private MatchChanges(Tournament tournament)
    {
        _before = tournament.Matches.Select(MatchDto.From).ToDictionary(m => m.Id);
    }

    public static MatchChanges Capture(Tournament tournament) => new(tournament);

    public IReadOnlyList<MatchDto> Since(Tournament tournament) =>
    [
        .. tournament.Matches
            .Select(MatchDto.From)
            .Where(after => !_before.TryGetValue(after.Id, out var before) || before != after)
            .OrderBy(m => m.Number),
    ];
}
