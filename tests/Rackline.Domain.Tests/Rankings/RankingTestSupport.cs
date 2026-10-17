using Rackline.Domain.Common;
using Rackline.Domain.Matches;
using Rackline.Domain.Tournaments;
using static Rackline.Domain.Tests.TestSupport;

namespace Rackline.Domain.Tests.Rankings;

internal static class RankingTestSupport
{
    public static readonly Guid OrganisationId = new("00000000-0000-0000-0000-0000000000bb");

    /// <summary>A tournament in progress with <paramref name="count"/> entries seeded 1 to n.</summary>
    public static Tournament Started(int count, TournamentFormat format, FormatOptions? options = null, Discipline discipline = Discipline.NineBall)
    {
        var tournament = new Tournament(OrganisationId, "Harbourside Open", discipline, format, MatchLength.RaceTo(3), options);
        tournament.OpenRegistration();
        for (var i = 1; i <= count; i++)
        {
            tournament.AddEntry(Guid.CreateVersion7(), seed: i);
        }

        tournament.MakeDraw(new SeededRandom(7));
        tournament.Start();
        return tournament;
    }

    /// <summary>Entry ids in seed order: index 0 is seed 1.</summary>
    public static IReadOnlyList<Guid> BySeed(Tournament tournament) =>
        [.. tournament.Entries.OrderBy(e => e.Seed).Select(e => e.Id)];

    /// <summary>Plays every match, better seed always winning, then completes the tournament.</summary>
    public static Tournament PlayedOut(int count, TournamentFormat format, FormatOptions? options = null)
    {
        var tournament = Started(count, format, options);
        PlayOut(tournament.Bracket, BetterSeedWins(BySeed(tournament)));
        tournament.Complete();
        return tournament;
    }

    /// <summary>Records a result for the next ready match, lowest number first.</summary>
    public static Match? PlayNext(Tournament tournament, bool slot1Wins, DateTimeOffset at)
    {
        if (tournament.Matches.Where(m => m.State == MatchState.Ready).MinBy(m => m.Number) is not { } match)
        {
            return null;
        }

        var win = match.FramesToWin;
        tournament.RecordResult(match.Number, slot1Wins ? win : win - 1, slot1Wins ? win - 1 : win, at);
        return match;
    }
}
