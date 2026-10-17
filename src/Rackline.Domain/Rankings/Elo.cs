using Rackline.Domain.Common;
using Rackline.Domain.Matches;
using Rackline.Domain.Tournaments;

namespace Rackline.Domain.Rankings;

/// <summary>A finished match that counts towards ratings, reduced to what the rating needs.</summary>
public sealed record RatedResult(Guid MatchId, Discipline Discipline, Guid WinnerPlayerId, Guid LoserPlayerId, DateTimeOffset At)
{
    /// <summary>
    /// The tournament's matches that count towards ratings: played to a result between two entries.
    /// Walkovers and byes do not count, as no frames were played.
    /// </summary>
    public static IEnumerable<RatedResult> From(Tournament tournament)
    {
        var players = tournament.Entries.ToDictionary(e => e.Id, e => e.PlayerId);
        return tournament.Matches
            .Where(m => m.State == MatchState.Completed && m.WinnerEntryId is not null && m.LoserEntryId is not null)
            .OrderBy(m => m.CompletedAt).ThenBy(m => m.Number)
            .Select(m => new RatedResult(
                m.Id, tournament.Discipline, players[m.WinnerEntryId!.Value], players[m.LoserEntryId!.Value], m.CompletedAt!.Value));
    }
}

/// <summary>
/// Elo ratings: each match moves both players by K × (result − expected result). Win or lose only;
/// the margin does not matter. Ratings start at <see cref="Rating.Initial"/>.
/// </summary>
public static class Elo
{
    /// <summary>Matches before a rating is settled.</summary>
    public const int ProvisionalMatches = 20;

    public const double ProvisionalK = 32;

    public const double SettledK = 16;

    public static double KFactor(int matchesPlayed) => matchesPlayed < ProvisionalMatches ? ProvisionalK : SettledK;

    /// <summary>The chance of winning, from 0 to 1, that the ratings predict.</summary>
    public static double Expected(double rating, double opponentRating) =>
        1 / (1 + Math.Pow(10, (opponentRating - rating) / 400));

    /// <summary>Updates both ratings for one result and returns the change to each.</summary>
    public static (RatingChange Winner, RatingChange Loser) Rate(Rating winner, Rating loser, Guid matchId, DateTimeOffset at)
    {
        Guard.That(winner.PlayerId != loser.PlayerId, "A player cannot be rated against themselves.");
        Guard.That(winner.Discipline == loser.Discipline, "Both ratings must be for the same discipline.");

        var (winnerBefore, loserBefore) = (winner.Value, loser.Value);
        winner.Apply(winnerBefore + KFactor(winner.MatchesPlayed) * (1 - Expected(winnerBefore, loserBefore)));
        loser.Apply(loserBefore + KFactor(loser.MatchesPlayed) * (0 - Expected(loserBefore, winnerBefore)));

        return (
            new RatingChange(winner.PlayerId, loser.PlayerId, winner.Discipline, matchId, true, winnerBefore, winner.Value, at),
            new RatingChange(loser.PlayerId, winner.PlayerId, loser.Discipline, matchId, false, loserBefore, loser.Value, at));
    }

    /// <summary>
    /// Rates a whole history from scratch, oldest first (ties keep the order given), and returns every
    /// rating and change. Rating results one at a time as they happen gives the same answer.
    /// </summary>
    public static (IReadOnlyList<Rating> Ratings, IReadOnlyList<RatingChange> Changes) Replay(IEnumerable<RatedResult> results)
    {
        var ratings = new Dictionary<(Guid, Discipline), Rating>();
        var changes = new List<RatingChange>();

        Rating For(Guid playerId, Discipline discipline)
        {
            if (!ratings.TryGetValue((playerId, discipline), out var rating))
            {
                rating = new Rating(playerId, discipline);
                ratings.Add((playerId, discipline), rating);
            }

            return rating;
        }

        foreach (var result in results.OrderBy(r => r.At))
        {
            var (winner, loser) = Rate(For(result.WinnerPlayerId, result.Discipline), For(result.LoserPlayerId, result.Discipline), result.MatchId, result.At);
            changes.Add(winner);
            changes.Add(loser);
        }

        return ([.. ratings.Values], changes);
    }
}
