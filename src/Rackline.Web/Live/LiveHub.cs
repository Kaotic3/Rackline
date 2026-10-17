using Microsoft.AspNetCore.SignalR;
using Rackline.Application.Live;

namespace Rackline.Web.Live;

/// <summary>
/// Live updates for spectators and scorers. Clients only choose what to watch; all scoring goes
/// through the authorised API, so the hub itself needs no authorisation.
/// </summary>
public sealed class LiveHub : Hub
{
    public Task WatchTournament(Guid tournamentId) =>
        Groups.AddToGroupAsync(Context.ConnectionId, LiveChannels.TournamentGroup(tournamentId));

    public Task UnwatchTournament(Guid tournamentId) =>
        Groups.RemoveFromGroupAsync(Context.ConnectionId, LiveChannels.TournamentGroup(tournamentId));

    public Task WatchMatch(Guid matchId) =>
        Groups.AddToGroupAsync(Context.ConnectionId, LiveChannels.MatchGroup(matchId));

    public Task UnwatchMatch(Guid matchId) =>
        Groups.RemoveFromGroupAsync(Context.ConnectionId, LiveChannels.MatchGroup(matchId));
}
