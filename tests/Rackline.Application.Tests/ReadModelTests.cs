using FluentAssertions;
using Rackline.Application.Players;
using Rackline.Application.Tournaments;
using Rackline.Domain.Common;
using Rackline.Domain.Matches;
using Rackline.Domain.Standings;
using Rackline.Domain.Tournaments;

namespace Rackline.Application.Tests;

public class GroupTablesTests
{
    private static readonly DateTimeOffset At = new(2026, 9, 1, 19, 0, 0, TimeSpan.Zero);

    private static TournamentDetailDto Detail(Tournament t) => new(
        t.Id, t.OrganisationId, t.Name, t.Discipline, t.Format, t.Status,
        new MatchLengthDto(t.MatchLength.Kind, t.MatchLength.Value), t.Options.GrandFinalReset, t.Options.GroupCount,
        [.. t.Entries.Select((e, i) => new EntryDto(e.Id, e.PlayerId, $"Player {i + 1}", e.Seed, e.Handicap))],
        [.. t.Matches.OrderBy(m => m.Number).Select(MatchDto.From)]);

    [Fact]
    public void Tables_match_the_domain_standings_for_each_group()
    {
        var tournament = new Tournament(Guid.CreateVersion7(), "Group Night", Discipline.TenBall, TournamentFormat.RoundRobin,
            MatchLength.RaceTo(3), new FormatOptions(GroupCount: 2));
        tournament.OpenRegistration();
        for (var i = 0; i < 8; i++)
        {
            tournament.AddEntry(Guid.CreateVersion7(), seed: i + 1);
        }

        tournament.MakeDraw(new SeededRandom(1));
        tournament.Start();
        foreach (var match in tournament.Matches.Where(m => m.Number % 3 != 0).ToList())
        {
            tournament.RecordResult(match.Number, match.Number % 2 == 0 ? 3 : 1, match.Number % 2 == 0 ? 2 : 3, At);
        }

        tournament.AwardWalkover(3, tournament.Matches.Single(m => m.Number == 3).Slot2.EntryId!.Value, At);

        var tables = GroupTables.For(Detail(tournament));

        tables.Select(t => t.Group).Should().Equal(1, 2);
        foreach (var table in tables)
        {
            var groupMatches = tournament.Matches.Where(m => m.Group == table.Group).ToList();
            var members = groupMatches.SelectMany(m => new[] { m.Slot1.EntryId!.Value, m.Slot2.EntryId!.Value }).Distinct()
                .OrderBy(id => tournament.Entries.Single(e => e.Id == id).Seed).ToList();
            var expected = GroupStandings.Calculate(members, groupMatches);

            table.Rows.Select(r => (r.Position, r.EntryId, r.Won, r.Lost, r.FramesFor, r.FramesAgainst))
                .Should().Equal(expected.Select(r => (r.Position, r.EntryId, r.Won, r.Lost, r.FramesFor, r.FramesAgainst)));
            table.Rows.Should().OnlyContain(r => r.PlayerName.StartsWith("Player "));
        }
    }

    [Fact]
    public void Knockout_tournaments_have_no_tables()
    {
        var tournament = new Tournament(Guid.CreateVersion7(), "Cup", Discipline.NineBall, TournamentFormat.SingleElimination, MatchLength.RaceTo(3));
        tournament.OpenRegistration();
        tournament.AddEntry(Guid.CreateVersion7());
        tournament.AddEntry(Guid.CreateVersion7());
        tournament.MakeDraw(new SeededRandom(1));

        GroupTables.For(Detail(tournament)).Should().BeEmpty();
    }
}

public class PlayerRecordTests
{
    private static readonly Guid Ada = Guid.CreateVersion7();
    private static readonly Guid Bram = Guid.CreateVersion7();

    private static PlayedMatchDto Played(Guid opponent, bool won, int framesFor, int framesAgainst, bool walkover = false, int breakAndRuns = 0) =>
        new(Guid.CreateVersion7(), Guid.CreateVersion7(), "Some Open", Discipline.NineBall, DateTimeOffset.UnixEpoch,
            opponent, opponent == Ada ? "Ada Marlow" : "Bram Keswick", won, walkover, framesFor, framesAgainst, breakAndRuns, 0);

    [Fact]
    public void Totals_matches_frames_and_break_and_runs()
    {
        var record = PlayerRecord.From(
        [
            Played(Ada, won: true, 7, 3, breakAndRuns: 2),
            Played(Bram, won: false, 4, 7),
            Played(Ada, won: true, 0, 0, walkover: true),
        ]);

        record.Should().BeEquivalentTo(new { Played = 3, Won = 2, Lost = 1, Walkovers = 1, FramesFor = 11, FramesAgainst = 10, BreakAndRuns = 2 });
        record.FrameDifference.Should().Be(1);
        record.WinPercentage.Should().Be(67);
    }

    [Fact]
    public void An_empty_record_has_no_win_percentage() =>
        PlayerRecord.From([]).WinPercentage.Should().BeNull();

    [Fact]
    public void Head_to_head_keeps_only_meetings_with_that_opponent()
    {
        var player = Guid.CreateVersion7();
        var matches = new[] { Played(Ada, true, 5, 1), Played(Bram, false, 2, 5), Played(Ada, false, 3, 5) };

        var h2h = HeadToHead.Between(player, Ada, matches);

        h2h.Meetings.Should().HaveCount(2).And.OnlyContain(m => m.OpponentPlayerId == Ada);
        h2h.Record.Should().BeEquivalentTo(new { Played = 2, Won = 1, Lost = 1, FramesFor = 8, FramesAgainst = 6 });
    }
}
