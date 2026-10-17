using Rackline.Application.Common;
using Rackline.Domain.Common;
using Rackline.Domain.Rankings;
using Rackline.Domain.Tournaments;

namespace Rackline.Application.Rankings;

/// <summary>Rates tournament results as they come in. The changes are saved with the tournament.</summary>
internal sealed class RatingUpdates(IRatingRepository ratings)
{
    private readonly Dictionary<(Guid, Discipline), Rating> _loaded = [];

    /// <summary>The matches already rated, to compare with after a command.</summary>
    public static IReadOnlySet<Guid> RatedSoFar(Tournament tournament) =>
        RatedResult.From(tournament).Select(r => r.MatchId).ToHashSet();

    public async Task RateNewResultsAsync(Tournament tournament, IReadOnlySet<Guid> ratedBefore, CancellationToken cancellationToken)
    {
        foreach (var result in RatedResult.From(tournament).Where(r => !ratedBefore.Contains(r.MatchId)))
        {
            var winner = await GetAsync(result.WinnerPlayerId, result.Discipline, cancellationToken);
            var loser = await GetAsync(result.LoserPlayerId, result.Discipline, cancellationToken);
            var (won, lost) = Elo.Rate(winner, loser, result.MatchId, result.At);
            ratings.Add(won);
            ratings.Add(lost);
        }
    }

    private async Task<Rating> GetAsync(Guid playerId, Discipline discipline, CancellationToken cancellationToken)
    {
        if (_loaded.TryGetValue((playerId, discipline), out var rating))
        {
            return rating;
        }

        rating = await ratings.FindAsync(playerId, discipline, cancellationToken);
        if (rating is null)
        {
            rating = new Rating(playerId, discipline);
            ratings.Add(rating);
        }

        _loaded[(playerId, discipline)] = rating;
        return rating;
    }
}
