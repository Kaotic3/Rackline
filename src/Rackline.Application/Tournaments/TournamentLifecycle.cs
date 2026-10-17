using Rackline.Application.Common;
using Rackline.Application.Live;
using Rackline.Domain.Common;
using Rackline.Domain.Rankings;
using Rackline.Domain.Standings;
using Rackline.Domain.Tournaments;

namespace Rackline.Application.Tournaments;

/// <summary>
/// Creating, changing and moving a tournament through its statuses. Completing a ranking tournament
/// awards its ranking points in the same save.
/// </summary>
public sealed class TournamentLifecycle(
    ITournamentRepository tournaments,
    IReferenceData references,
    IRankingRepository rankings,
    ILiveNotifier notifier)
{
    public async Task<Guid> CreateAsync(Guid organisationId, TournamentSettings settings, CancellationToken cancellationToken = default)
    {
        if (!await references.OrganisationExistsAsync(organisationId, cancellationToken))
        {
            throw new NotFoundException("Organisation", organisationId);
        }

        var tournament = new Tournament(
            organisationId, settings.Name, settings.Discipline, settings.Format, settings.ToMatchLength(), settings.ToOptions());
        tournament.CountTowards(await FindRankingAsync(settings.RankingId, cancellationToken));
        tournaments.Add(tournament);
        await tournaments.SaveChangesAsync(cancellationToken);
        return tournament.Id;
    }

    public Task UpdateAsync(Guid id, TournamentSettings settings, CancellationToken cancellationToken = default) =>
        ApplyAsync(id, async t =>
        {
            var ranking = await FindRankingAsync(settings.RankingId, cancellationToken);

            // The settings replace everything, so leave the old ranking before any change of discipline.
            t.Rename(settings.Name);
            t.CountTowards(null);
            t.Reconfigure(settings.Discipline, settings.Format, settings.ToMatchLength(), settings.ToOptions());
            t.CountTowards(ranking);
        }, cancellationToken);

    public async Task DeleteAsync(Guid id, CancellationToken cancellationToken = default)
    {
        var tournament = await tournaments.GetAsync(id, cancellationToken);
        if (!tournament.CanBeDeleted)
        {
            throw new DomainException("A tournament with a draw in play cannot be deleted. Cancel it first.");
        }

        tournaments.Remove(tournament);
        await tournaments.SaveChangesAsync(cancellationToken);
    }

    public Task OpenRegistrationAsync(Guid id, CancellationToken cancellationToken = default) =>
        ApplyAsync(id, t => t.OpenRegistration(), cancellationToken);

    public Task StartAsync(Guid id, CancellationToken cancellationToken = default) =>
        ApplyAsync(id, t => t.Start(), cancellationToken);

    public Task CompleteAsync(Guid id, CancellationToken cancellationToken = default) =>
        ApplyAsync(id, async t =>
        {
            t.Complete();
            if (await FindRankingAsync(t.RankingId, cancellationToken) is { } ranking)
            {
                var players = t.Entries.ToDictionary(e => e.Id, e => e.PlayerId);
                ranking.AwardTournament(t.Id, FinishingPositions.Calculate(t).Select(p => (players[p.EntryId], p.Position)));
            }
        }, cancellationToken);

    public Task CancelAsync(Guid id, CancellationToken cancellationToken = default) =>
        ApplyAsync(id, t => t.Cancel(), cancellationToken);

    private Task ApplyAsync(Guid id, Action<Tournament> change, CancellationToken cancellationToken) =>
        ApplyAsync(id, t =>
        {
            change(t);
            return Task.CompletedTask;
        }, cancellationToken);

    private async Task ApplyAsync(Guid id, Func<Tournament, Task> change, CancellationToken cancellationToken)
    {
        var tournament = await tournaments.GetAsync(id, cancellationToken);
        await change(tournament);
        await tournaments.SaveChangesAsync(cancellationToken);
        await notifier.TournamentChangedAsync(id, tournament.Status, cancellationToken);
    }

    private async Task<Ranking?> FindRankingAsync(Guid? rankingId, CancellationToken cancellationToken) =>
        rankingId is { } id
            ? await rankings.FindAsync(id, cancellationToken) ?? throw new NotFoundException("Ranking", id)
            : null;
}
