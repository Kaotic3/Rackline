using FluentAssertions;
using Rackline.Application.Common;
using Rackline.Application.Rankings;
using Rackline.Application.Tournaments;
using Rackline.Domain.Common;
using Rackline.Domain.Matches;
using Rackline.Domain.Rankings;
using Rackline.Domain.Tournaments;

namespace Rackline.Application.Tests;

public class RankingUseCaseTests
{
    private static readonly DateTimeOffset Now = new(2026, 9, 1, 19, 0, 0, TimeSpan.Zero);

    private readonly InMemoryTournaments _tournaments = new();
    private readonly InMemoryRankings _rankings = new();
    private readonly InMemoryRatings _ratings = new();
    private readonly FakeReferences _references = new();
    private readonly RecordingNotifier _notifier = new();
    private readonly FixedTime _time = new(Now);
    private readonly Guid _organisationId = Guid.CreateVersion7();

    private readonly TournamentLifecycle _lifecycle;
    private readonly TournamentEntries _entries;
    private readonly TournamentDraws _draws;
    private readonly MatchResults _results;
    private readonly LiveScoring _scoring;
    private readonly RankingSetup _setup;

    public RankingUseCaseTests()
    {
        _references.Organisations.Add(_organisationId);
        _lifecycle = new TournamentLifecycle(_tournaments, _references, _rankings, _notifier);
        _entries = new TournamentEntries(_tournaments, _references, _notifier);
        _draws = new TournamentDraws(_tournaments, _notifier);
        _results = new MatchResults(_tournaments, _references, _ratings, _notifier, _time);
        _scoring = new LiveScoring(_tournaments, _ratings, _notifier, _time);
        _setup = new RankingSetup(_rankings, _references);
    }

    private static TournamentSettings Settings(Guid? rankingId = null, Discipline discipline = Discipline.NineBall) =>
        new("Harbour Lights Open", discipline, TournamentFormat.SingleElimination, MatchLengthKind.RaceTo, 3, RankingId: rankingId);

    private Task<Guid> RankingAsync(Discipline discipline = Discipline.NineBall) =>
        _setup.CreateAsync(_organisationId, "Coast 9-ball rankings", discipline, "2026", [50, 30, 20], 5);

    private Tournament Saved(Guid id) => _tournaments.Saved.Single(t => t.Id == id);

    /// <summary>A drawn, started tournament with <paramref name="players"/> seeded players.</summary>
    private async Task<(Guid Id, List<Guid> Players)> StartedAsync(int players, Guid? rankingId = null)
    {
        var id = await _lifecycle.CreateAsync(_organisationId, Settings(rankingId));
        await _lifecycle.OpenRegistrationAsync(id);
        var playerIds = new List<Guid>();
        for (var i = 0; i < players; i++)
        {
            var player = Guid.CreateVersion7();
            _references.Players.Add(player);
            playerIds.Add(player);
            await _entries.AddAsync(id, player, seed: i + 1, handicap: 0);
        }

        await _draws.MakeAsync(id, 1);
        await _lifecycle.StartAsync(id);
        return (id, playerIds);
    }

    private Guid PlayerOf(Guid tournamentId, Guid? entryId) => Saved(tournamentId).Entries.Single(e => e.Id == entryId).PlayerId;

    /// <summary>Plays every match with the better seed (slot 1 in this draw order) winning 3–1.</summary>
    private async Task PlayOutAsync(Guid id)
    {
        while (Saved(id).Matches.Where(m => m.State == MatchState.Ready).MinBy(m => m.Number) is { } match)
        {
            var seeds = Saved(id).Entries.ToDictionary(e => e.Id, e => e.Seed);
            var slot1Better = seeds[match.Slot1.EntryId!.Value] < seeds[match.Slot2.EntryId!.Value];
            await _results.RecordResultAsync(id, match.Number, slot1Better ? 3 : 1, slot1Better ? 1 : 3);
            _time.Now = _time.Now.AddMinutes(30);
        }
    }

    [Fact]
    public async Task A_recorded_result_rates_both_players()
    {
        var (id, _) = await StartedAsync(2);
        var match = Saved(id).Matches.Single();

        await _results.RecordResultAsync(id, match.Number, 3, 1);

        var winner = PlayerOf(id, match.WinnerEntryId);
        var loser = PlayerOf(id, match.LoserEntryId);
        _ratings.For(winner, Discipline.NineBall)!.Value.Should().Be(1516);
        _ratings.For(loser, Discipline.NineBall)!.Value.Should().Be(1484);
        _ratings.Changes.Should().HaveCount(2).And.OnlyContain(c => c.MatchId == match.Id && c.At == Now);
    }

    [Fact]
    public async Task The_deciding_frame_rates_the_match_and_earlier_frames_do_not()
    {
        var (id, _) = await StartedAsync(2);
        var number = Saved(id).Matches.Single().Number;
        await _results.StartAsync(id, number, null);

        await _scoring.RecordFrameAsync(id, number, 2, false, null, null);
        await _scoring.RecordFrameAsync(id, number, 2, false, null, null);
        _ratings.Changes.Should().BeEmpty();

        await _scoring.RecordFrameAsync(id, number, 2, false, null, null);

        var match = Saved(id).Matches.Single();
        _ratings.For(PlayerOf(id, match.Slot2.EntryId), Discipline.NineBall)!.Value.Should().Be(1516);
        _ratings.Changes.Should().HaveCount(2);
    }

    [Fact]
    public async Task Walkovers_and_byes_are_not_rated()
    {
        var (id, _) = await StartedAsync(3);
        var ready = Saved(id).Matches.Single(m => m.State == MatchState.Ready);

        await _results.AwardWalkoverAsync(id, ready.Number, ready.Slot1.EntryId!.Value);

        _ratings.Ratings.Should().BeEmpty();
        _ratings.Changes.Should().BeEmpty();
    }

    [Fact]
    public async Task Existing_ratings_carry_on_from_where_they_were()
    {
        var (first, _) = await StartedAsync(2);
        var match = Saved(first).Matches.Single();
        await _results.RecordResultAsync(first, match.Number, 3, 0);
        var winner = PlayerOf(first, match.WinnerEntryId);

        _ratings.For(winner, Discipline.NineBall)!.MatchesPlayed.Should().Be(1);
        var before = _ratings.For(winner, Discipline.NineBall)!.Value;

        // The same player, seeded first, wins another tournament.
        var second = await _lifecycle.CreateAsync(_organisationId, Settings());
        await _lifecycle.OpenRegistrationAsync(second);
        var newcomer = Guid.CreateVersion7();
        _references.Players.UnionWith([winner, newcomer]);
        await _entries.AddAsync(second, winner, 1, 0);
        await _entries.AddAsync(second, newcomer, 2, 0);
        await _draws.MakeAsync(second, 1);
        await _lifecycle.StartAsync(second);
        await PlayOutAsync(second);

        var rating = _ratings.For(winner, Discipline.NineBall)!;
        rating.MatchesPlayed.Should().Be(2);
        rating.Value.Should().BeApproximately(before + 32 * (1 - Elo.Expected(before, 1500)), 1e-9);
        _ratings.Ratings.Should().HaveCount(3);
    }

    [Fact]
    public async Task Completing_a_ranking_tournament_awards_points_by_finishing_position()
    {
        var rankingId = await RankingAsync();
        var (id, players) = await StartedAsync(4, rankingId);
        await PlayOutAsync(id);

        await _lifecycle.CompleteAsync(id);

        var ranking = _rankings.Saved.Single();
        ranking.Awards.Select(a => (a.PlayerId, a.Position, a.Points)).Should().BeEquivalentTo(new[]
        {
            (players[0], 1, 50),
            (players[1], 2, 30),
            (players[2], 3, 20),
            (players[3], 3, 20),
        });
        _notifier.Statuses.Last().Should().Be(TournamentStatus.Completed);
    }

    [Fact]
    public async Task Completing_a_tournament_outside_any_ranking_awards_nothing()
    {
        await RankingAsync();
        var (id, _) = await StartedAsync(4);
        await PlayOutAsync(id);

        await _lifecycle.CompleteAsync(id);

        _rankings.Saved.Single().Awards.Should().BeEmpty();
        Saved(id).Status.Should().Be(TournamentStatus.Completed);
    }

    [Fact]
    public async Task A_tournament_can_join_and_leave_a_ranking_before_the_draw()
    {
        var rankingId = await RankingAsync();
        var id = await _lifecycle.CreateAsync(_organisationId, Settings());

        await _lifecycle.UpdateAsync(id, Settings(rankingId));
        Saved(id).RankingId.Should().Be(rankingId);

        // Changing the discipline and leaving the ranking can be done in one update.
        await _lifecycle.UpdateAsync(id, Settings(discipline: Discipline.TenBall));
        Saved(id).Should().BeEquivalentTo(new { RankingId = (Guid?)null, Discipline = Discipline.TenBall }, o => o.ExcludingMissingMembers());
    }

    [Fact]
    public async Task The_ranking_must_exist_and_match_the_discipline()
    {
        var snookerRanking = await RankingAsync(Discipline.Snooker);

        var unknown = () => _lifecycle.CreateAsync(_organisationId, Settings(Guid.CreateVersion7()));
        (await unknown.Should().ThrowAsync<NotFoundException>()).Which.Resource.Should().Be("Ranking");

        var mismatch = () => _lifecycle.CreateAsync(_organisationId, Settings(snookerRanking));
        await mismatch.Should().ThrowAsync<DomainException>();
        _tournaments.Saved.Should().BeEmpty();
    }

    [Fact]
    public async Task Rankings_need_a_known_organisation()
    {
        var act = () => _setup.CreateAsync(Guid.CreateVersion7(), "Rankings", Discipline.NineBall, "2026", [10], 0);

        (await act.Should().ThrowAsync<NotFoundException>()).Which.Resource.Should().Be("Organisation");
    }

    [Fact]
    public async Task Rebuilding_gives_the_same_ratings_as_rating_results_as_they_happen()
    {
        var (id, _) = await StartedAsync(8);
        await PlayOutAsync(id);
        var live = _ratings.Ratings.Select(r => (r.PlayerId, r.Value, r.MatchesPlayed)).ToList();
        _ratings.History.AddRange(RatedResult.From(Saved(id)));

        var rated = await new RatingRebuild(_ratings).RebuildAsync();

        rated.Should().Be(7);
        _ratings.Ratings.Select(r => (r.PlayerId, r.Value, r.MatchesPlayed)).Should().BeEquivalentTo(live);
        _ratings.Changes.Should().HaveCount(14);
    }
}
