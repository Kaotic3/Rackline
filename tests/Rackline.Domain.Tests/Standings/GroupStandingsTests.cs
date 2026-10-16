using FluentAssertions;
using Rackline.Domain.Draws;
using Rackline.Domain.Matches;
using Rackline.Domain.Standings;
using static Rackline.Domain.Tests.TestSupport;

namespace Rackline.Domain.Tests.Standings;

public class GroupStandingsTests
{
    private readonly IReadOnlyList<Guid> _entries = Entries(4);
    private readonly Bracket _bracket;

    public GroupStandingsTests()
    {
        _bracket = new Bracket(new RoundRobinEngine().Generate(TournamentId, _entries, RaceTo5));
    }

    private Guid A => _entries[0];

    private Guid B => _entries[1];

    private Guid C => _entries[2];

    private Guid D => _entries[3];

    /// <summary>Records <paramref name="winner"/> beating <paramref name="loser"/> by the given frame score.</summary>
    private void Beat(Guid winner, Guid loser, int loserFrames)
    {
        var match = _bracket.Matches.Single(m => m.Involves(winner) && m.Involves(loser));
        if (match.Slot1.EntryId == winner)
        {
            _bracket.RecordResult(match.Number, 5, loserFrames, At);
        }
        else
        {
            _bracket.RecordResult(match.Number, loserFrames, 5, At);
        }
    }

    private IReadOnlyList<StandingRow> Table() => GroupStandings.Calculate(_entries, _bracket.Matches);

    [Fact]
    public void Before_any_results_the_table_follows_draw_order()
    {
        Table().Select(r => r.EntryId).Should().Equal(_entries);
        Table().Should().OnlyContain(r => r.Played == 0);
    }

    [Fact]
    public void Ranks_by_wins_and_tallies_frames()
    {
        Beat(D, A, 2);
        Beat(D, B, 0);
        Beat(D, C, 4);
        Beat(C, A, 1);
        Beat(C, B, 3);
        Beat(B, A, 4);

        var table = Table();

        table.Select(r => r.EntryId).Should().Equal(D, C, B, A);
        table[0].Should().BeEquivalentTo(new { Position = 1, Played = 3, Won = 3, Lost = 0, FramesFor = 15, FramesAgainst = 6 });
        table[0].FrameDifference.Should().Be(9);
        table[3].Should().BeEquivalentTo(new { Position = 4, Won = 0, Lost = 3, FramesFor = 7, FramesAgainst = 15 });
    }

    [Fact]
    public void Two_way_ties_are_broken_by_head_to_head()
    {
        // A and B both win twice; B has the better frame difference but A won their meeting.
        Beat(A, B, 4);
        Beat(B, C, 0);
        Beat(B, D, 0);
        Beat(A, C, 4);
        Beat(D, A, 4);
        Beat(C, D, 4);

        Table().Take(2).Select(r => r.EntryId).Should().Equal(A, B);
    }

    [Fact]
    public void Three_way_ties_fall_back_to_frame_difference()
    {
        // A, B and C each win two; D wins none.
        Beat(A, B, 4);
        Beat(B, C, 4);
        Beat(C, A, 4);
        Beat(A, D, 0);
        Beat(B, D, 3);
        Beat(C, D, 2);

        Table().Select(r => r.EntryId).Should().Equal(A, C, B, D);
    }

    [Fact]
    public void Walkovers_count_as_wins_without_frames()
    {
        var match = _bracket.Matches.Single(m => m.Involves(A) && m.Involves(B));
        _bracket.AwardWalkover(match.Number, B, At);

        var rowB = Table().Single(r => r.EntryId == B);

        rowB.Should().BeEquivalentTo(new { Position = 1, Played = 1, Won = 1, FramesFor = 0, FramesAgainst = 0 });
        Table().Single(r => r.EntryId == A).Lost.Should().Be(1);
    }
}
