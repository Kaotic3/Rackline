using Rackline.Application.Common;
using Rackline.Domain.Common;
using Rackline.Domain.Organisations;
using Rackline.Domain.Rankings;

namespace Rackline.Application.Rankings;

/// <summary>Setting up rankings. Points are awarded when a tournament that counts towards one is completed.</summary>
public sealed class RankingSetup(IRankingRepository rankings, IReferenceData references, OrganisationAccess access)
{
    public async Task<Guid> CreateAsync(
        Guid organisationId,
        string name,
        Discipline discipline,
        string season,
        IReadOnlyList<int> pointsByPosition,
        int participationPoints,
        CancellationToken cancellationToken = default)
    {
        if (!await references.OrganisationExistsAsync(organisationId, cancellationToken))
        {
            throw new NotFoundException("Organisation", organisationId);
        }

        await access.EnsureAsync(organisationId, StaffRole.Organiser, cancellationToken);
        var ranking = new Ranking(organisationId, name, discipline, season, pointsByPosition, participationPoints);
        rankings.Add(ranking);
        await rankings.SaveChangesAsync(cancellationToken);
        return ranking.Id;
    }
}

/// <summary>Works out every rating again from the full history of results.</summary>
public sealed class RatingRebuild(IRatingRepository ratings)
{
    /// <summary>Returns the number of results rated.</summary>
    public async Task<int> RebuildAsync(CancellationToken cancellationToken = default)
    {
        var results = await ratings.ListRatedResultsAsync(cancellationToken);
        var (rebuilt, changes) = Elo.Replay(results);
        await ratings.ReplaceAllAsync(rebuilt, changes, cancellationToken);
        return results.Count;
    }
}
