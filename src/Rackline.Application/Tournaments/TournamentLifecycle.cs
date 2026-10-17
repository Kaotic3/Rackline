using Rackline.Application.Common;
using Rackline.Domain.Common;
using Rackline.Domain.Tournaments;

namespace Rackline.Application.Tournaments;

/// <summary>Creating, changing and moving a tournament through its statuses.</summary>
public sealed class TournamentLifecycle(ITournamentRepository tournaments, IReferenceData references)
{
    public async Task<Guid> CreateAsync(Guid organisationId, TournamentSettings settings, CancellationToken cancellationToken = default)
    {
        if (!await references.OrganisationExistsAsync(organisationId, cancellationToken))
        {
            throw new NotFoundException("Organisation", organisationId);
        }

        var tournament = new Tournament(
            organisationId, settings.Name, settings.Discipline, settings.Format, settings.ToMatchLength(), settings.ToOptions());
        tournaments.Add(tournament);
        await tournaments.SaveChangesAsync(cancellationToken);
        return tournament.Id;
    }

    public Task UpdateAsync(Guid id, TournamentSettings settings, CancellationToken cancellationToken = default) =>
        ApplyAsync(id, t =>
        {
            t.Rename(settings.Name);
            t.Reconfigure(settings.Discipline, settings.Format, settings.ToMatchLength(), settings.ToOptions());
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
        ApplyAsync(id, t => t.Complete(), cancellationToken);

    public Task CancelAsync(Guid id, CancellationToken cancellationToken = default) =>
        ApplyAsync(id, t => t.Cancel(), cancellationToken);

    private async Task ApplyAsync(Guid id, Action<Tournament> change, CancellationToken cancellationToken)
    {
        change(await tournaments.GetAsync(id, cancellationToken));
        await tournaments.SaveChangesAsync(cancellationToken);
    }
}
