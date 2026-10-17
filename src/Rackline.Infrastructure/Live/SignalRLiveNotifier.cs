using Microsoft.AspNetCore.SignalR;
using Microsoft.Extensions.Logging;
using Rackline.Application.Live;
using Rackline.Application.Tournaments;
using Rackline.Domain.Tournaments;

namespace Rackline.Infrastructure.Live;

/// <summary>
/// Broadcasts changes to SignalR groups and to the in-process <see cref="LiveFeed"/>.
/// A failed broadcast is logged but never fails the command that caused it: the change is already saved.
/// </summary>
public sealed class SignalRLiveNotifier<THub>(IHubContext<THub> hub, LiveFeed feed, ILogger<SignalRLiveNotifier<THub>> logger) : ILiveNotifier
    where THub : Hub
{
    public async Task MatchesChangedAsync(Guid tournamentId, IReadOnlyList<MatchDto> matches, CancellationToken cancellationToken)
    {
        if (matches.Count == 0)
        {
            return;
        }

        await BroadcastAsync(tournamentId, new LiveUpdate(tournamentId, null, matches), async () =>
        {
            await hub.Clients.Group(LiveChannels.TournamentGroup(tournamentId))
                .SendAsync(LiveChannels.MatchesUpdated, tournamentId, matches, cancellationToken);

            foreach (var match in matches)
            {
                await hub.Clients.Group(LiveChannels.MatchGroup(match.Id))
                    .SendAsync(LiveChannels.MatchUpdated, match, cancellationToken);
            }
        });
    }

    public Task TournamentChangedAsync(Guid tournamentId, TournamentStatus status, CancellationToken cancellationToken) =>
        BroadcastAsync(tournamentId, new LiveUpdate(tournamentId, status, []), () =>
            hub.Clients.Group(LiveChannels.TournamentGroup(tournamentId))
                .SendAsync(LiveChannels.TournamentUpdated, tournamentId, status, cancellationToken));

    private async Task BroadcastAsync(Guid tournamentId, LiveUpdate update, Func<Task> send)
    {
        try
        {
            feed.Publish(update);
            await send();
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            logger.LogWarning(ex, "Could not broadcast a live update for tournament {TournamentId}", tournamentId);
        }
    }
}
