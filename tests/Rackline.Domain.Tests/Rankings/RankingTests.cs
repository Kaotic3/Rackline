using FluentAssertions;
using Rackline.Domain.Common;
using Rackline.Domain.Organisations;
using Rackline.Domain.Rankings;
using Rackline.Domain.Tournaments;
using static Rackline.Domain.Tests.Rankings.RankingTestSupport;

namespace Rackline.Domain.Tests.Rankings;

public class RankingTests
{
    private static readonly Guid Ann = new("00000000-0000-0000-0000-000000000001");
    private static readonly Guid Ben = new("00000000-0000-0000-0000-000000000002");
    private static readonly Guid Cal = new("00000000-0000-0000-0000-000000000003");
    private static readonly Guid Dee = new("00000000-0000-0000-0000-000000000004");

    private static readonly Guid NationalId = new("00000000-0000-0000-0000-0000000000cc");

    /// <summary>The tournament's organisation, under a national body.</summary>
    private static readonly OrganisationLineage Lineage = new(OrganisationId, new Dictionary<Guid, Guid?>
    {
        [OrganisationId] = NationalId,
        [NationalId] = null,
    });

    private static Ranking NewRanking(Discipline discipline = Discipline.NineBall) =>
        new(OrganisationId, "Coastal 9-ball rankings", discipline, "2026", [100, 70, 50, 30], participationPoints: 10);

    [Fact]
    public void Points_follow_the_table_then_participation()
    {
        var ranking = NewRanking();

        ranking.PointsFor(1).Should().Be(100);
        ranking.PointsFor(3).Should().Be(50);
        ranking.PointsFor(4).Should().Be(30);
        ranking.PointsFor(5).Should().Be(10);
        ranking.PointsFor(99).Should().Be(10);
        FluentActions.Invoking(() => ranking.PointsFor(0)).Should().Throw<DomainException>();
    }

    [Theory]
    [InlineData(new int[0], 0)]
    [InlineData(new[] { 50, 70 }, 0)]
    [InlineData(new[] { 50, -1 }, 0)]
    [InlineData(new[] { 50, 20 }, 30)]
    [InlineData(new[] { 50 }, -5)]
    public void Rejects_a_points_table_that_is_empty_negative_or_rises(int[] points, int participation)
    {
        FluentActions.Invoking(() => new Ranking(OrganisationId, "Rankings", Discipline.NineBall, "2026", points, participation))
            .Should().Throw<DomainException>();
    }

    [Fact]
    public void Needs_a_name_season_and_organisation()
    {
        FluentActions.Invoking(() => new Ranking(OrganisationId, " ", Discipline.NineBall, "2026", [10])).Should().Throw<DomainException>();
        FluentActions.Invoking(() => new Ranking(OrganisationId, "Rankings", Discipline.NineBall, "", [10])).Should().Throw<DomainException>();
        FluentActions.Invoking(() => new Ranking(Guid.Empty, "Rankings", Discipline.NineBall, "2026", [10])).Should().Throw<DomainException>();
    }

    [Fact]
    public void Awards_points_by_position_and_totals_them_across_tournaments()
    {
        var ranking = NewRanking();
        var first = Guid.CreateVersion7();
        var second = Guid.CreateVersion7();

        ranking.AwardTournament(first, [(Ann, 1), (Ben, 2), (Cal, 3), (Dee, 3)]);
        ranking.AwardTournament(second, [(Ben, 1), (Ann, 2), (Cal, 5)]);

        ranking.Awards.Should().HaveCount(7);
        ranking.HasAwarded(first).Should().BeTrue();
        ranking.TotalFor(Ann).Should().Be(170);
        ranking.TotalFor(Ben).Should().Be(170);
        ranking.TotalFor(Cal).Should().Be(60);
        ranking.TotalFor(Dee).Should().Be(50);

        ranking.Table().Should().Equal(
            new RankingRow(1, Ann, 170, 2),
            new RankingRow(1, Ben, 170, 2),
            new RankingRow(3, Cal, 60, 2),
            new RankingRow(4, Dee, 50, 1));
    }

    [Fact]
    public void A_tournament_counts_only_once()
    {
        var ranking = NewRanking();
        var tournamentId = Guid.CreateVersion7();
        ranking.AwardTournament(tournamentId, [(Ann, 1)]);

        ranking.Invoking(r => r.AwardTournament(tournamentId, [(Ben, 1)])).Should().Throw<DomainException>();
        ranking.Invoking(r => r.AwardTournament(Guid.CreateVersion7(), [(Ben, 1), (Ben, 2)])).Should().Throw<DomainException>();
        ranking.Awards.Should().ContainSingle();
    }

    [Fact]
    public void A_tournament_counts_towards_a_ranking_of_its_own_organisation_or_one_above_it()
    {
        var tournament = new Tournament(OrganisationId, "Harbourside Open", Discipline.NineBall, TournamentFormat.SingleElimination, MatchLength.RaceTo(5));
        var ranking = NewRanking();
        var national = new Ranking(NationalId, "National 9-ball rankings", Discipline.NineBall, "2026", [10]);

        tournament.CountTowards(ranking, Lineage);
        tournament.RankingId.Should().Be(ranking.Id);
        tournament.CountTowards(national, Lineage);
        tournament.RankingId.Should().Be(national.Id);

        tournament.Invoking(t => t.CountTowards(NewRanking(Discipline.Snooker), Lineage)).Should().Throw<DomainException>();
        tournament.Invoking(t => t.CountTowards(new Ranking(Guid.CreateVersion7(), "Elsewhere", Discipline.NineBall, "2026", [10]), Lineage))
            .Should().Throw<DomainException>();

        tournament.CountTowards(null, Lineage);
        tournament.RankingId.Should().BeNull();
    }

    [Fact]
    public void The_lineage_must_start_at_the_tournament_organisation()
    {
        var tournament = new Tournament(NationalId, "National Open", Discipline.NineBall, TournamentFormat.SingleElimination, MatchLength.RaceTo(5));

        // A national event cannot count towards a club's ranking by passing the club's lineage.
        tournament.Invoking(t => t.CountTowards(NewRanking(), Lineage)).Should().Throw<DomainException>();
    }

    [Fact]
    public void The_discipline_cannot_change_while_the_tournament_is_in_a_ranking()
    {
        var tournament = new Tournament(OrganisationId, "Harbourside Open", Discipline.NineBall, TournamentFormat.SingleElimination, MatchLength.RaceTo(5));
        tournament.CountTowards(NewRanking(), Lineage);

        tournament.Invoking(t => t.Reconfigure(Discipline.TenBall, TournamentFormat.SingleElimination, MatchLength.RaceTo(5)))
            .Should().Throw<DomainException>();
        tournament.Reconfigure(Discipline.NineBall, TournamentFormat.DoubleElimination, MatchLength.RaceTo(7));
        tournament.Format.Should().Be(TournamentFormat.DoubleElimination);
    }

    [Fact]
    public void The_ranking_is_fixed_once_the_draw_is_made()
    {
        var tournament = Started(4, TournamentFormat.SingleElimination);

        tournament.Invoking(t => t.CountTowards(NewRanking(), new OrganisationLineage(OrganisationId, new Dictionary<Guid, Guid?> { [OrganisationId] = null })))
            .Should().Throw<DomainException>();
        tournament.Invoking(t => t.LeaveRanking()).Should().Throw<DomainException>();
    }
}
