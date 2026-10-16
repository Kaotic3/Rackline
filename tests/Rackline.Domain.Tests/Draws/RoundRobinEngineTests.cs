using FluentAssertions;
using Rackline.Domain.Common;
using Rackline.Domain.Draws;
using Rackline.Domain.Matches;
using static Rackline.Domain.Tests.TestSupport;

namespace Rackline.Domain.Tests.Draws;

public class RoundRobinEngineTests
{
    public static IEnumerable<object[]> FieldsAndGroups =>
        from n in Enumerable.Range(2, 47)
        from g in new[] { 1, 2, 3, 4, 8 }
        where n >= g * 2
        select new object[] { n, g };

    [Theory]
    [MemberData(nameof(FieldsAndGroups))]
    public void Every_pair_in_a_group_meets_exactly_once(int n, int groupCount)
    {
        var entries = Entries(n);
        var matches = new RoundRobinEngine(groupCount).Generate(TournamentId, entries, RaceTo5);
        var groups = RoundRobinEngine.AssignGroups(entries, groupCount);

        matches.Should().HaveCount(groups.Sum(g => g.Count * (g.Count - 1) / 2));
        matches.Select(m => m.Number).Should().Equal(Enumerable.Range(1, matches.Count));
        matches.Should().OnlyContain(m => m.Side == BracketSide.Group && m.State == MatchState.Ready && m.WinnerTo == null);

        for (var g = 0; g < groups.Count; g++)
        {
            var members = groups[g];
            var groupMatches = matches.Where(m => m.Group == g + 1).ToList();
            var pairings = groupMatches.Select(m => Pair(m.Slot1.EntryId!.Value, m.Slot2.EntryId!.Value)).ToList();

            var expected = (from a in members from b in members where a.CompareTo(b) < 0 select (a, b)).ToHashSet();
            pairings.Should().HaveCount(expected.Count);
            pairings.ToHashSet().SetEquals(expected).Should().BeTrue("each pair in the group meets exactly once");
        }
    }

    [Theory]
    [MemberData(nameof(FieldsAndGroups))]
    public void Nobody_plays_twice_in_a_round_and_the_round_count_is_minimal(int n, int groupCount)
    {
        var entries = Entries(n);
        var matches = new RoundRobinEngine(groupCount).Generate(TournamentId, entries, RaceTo5);

        foreach (var group in matches.GroupBy(m => m.Group))
        {
            var size = RoundRobinEngine.AssignGroups(entries, groupCount)[group.Key!.Value - 1].Count;
            group.Select(m => m.Round).Distinct().Should().HaveCount(size % 2 == 0 ? size - 1 : size);

            foreach (var round in group.GroupBy(m => m.Round))
            {
                round.SelectMany(m => new[] { m.Slot1.EntryId, m.Slot2.EntryId }).Should().OnlyHaveUniqueItems();
                round.Select(m => m.Position).Should().Equal(Enumerable.Range(1, round.Count()));
            }
        }
    }

    [Fact]
    public void Odd_groups_give_each_entry_one_round_off()
    {
        var entries = Entries(5);
        var matches = new RoundRobinEngine().Generate(TournamentId, entries, RaceTo5);

        foreach (var entry in entries)
        {
            var roundsPlayed = matches.Where(m => m.Involves(entry)).Select(m => m.Round).ToList();
            roundsPlayed.Should().HaveCount(4).And.OnlyHaveUniqueItems();
        }
    }

    [Fact]
    public void Groups_are_filled_in_a_snake_by_seed()
    {
        var entries = Entries(8);

        var groups = RoundRobinEngine.AssignGroups(entries, 3);

        groups[0].Should().Equal(entries[0], entries[5], entries[6]);
        groups[1].Should().Equal(entries[1], entries[4], entries[7]);
        groups[2].Should().Equal(entries[2], entries[3]);
    }

    [Theory]
    [MemberData(nameof(FieldsAndGroups))]
    public void Groups_are_balanced_and_top_seeds_are_split(int n, int groupCount)
    {
        var entries = Entries(n);

        var groups = RoundRobinEngine.AssignGroups(entries, groupCount);

        groups.Should().HaveCount(groupCount);
        (groups.Max(g => g.Count) - groups.Min(g => g.Count)).Should().BeLessThanOrEqualTo(1);
        groups.SelectMany(g => g).Should().BeEquivalentTo(entries);
        groups.Select(g => g[0]).Should().Equal(entries.Take(groupCount), "seeds 1..g head separate groups");
    }

    [Fact]
    public void Rejects_groups_with_fewer_than_two_entries()
    {
        var engine = new RoundRobinEngine(groupCount: 3);

        engine.Invoking(e => e.Generate(TournamentId, Entries(5), RaceTo5)).Should().Throw<DomainException>();
        FluentActions.Invoking(() => new RoundRobinEngine(0)).Should().Throw<DomainException>();
    }

    [Fact]
    public void Results_do_not_move_anyone_anywhere()
    {
        var bracket = new Bracket(new RoundRobinEngine().Generate(TournamentId, Entries(4), RaceTo5));

        PlayOut(bracket, _ => true);

        bracket.IsFinished.Should().BeTrue();
        bracket.ChampionEntryId.Should().BeNull("a round robin is decided by its table");
    }

    [Fact]
    public void Generation_is_deterministic()
    {
        var entries = Entries(13);
        var engine = new RoundRobinEngine(groupCount: 2);

        var first = engine.Generate(TournamentId, entries, RaceTo5);
        var second = engine.Generate(TournamentId, entries, RaceTo5);

        first.Select(Describe)
            .Should().Equal(second.Select(Describe));
    }

    private static (Guid, Guid) Pair(Guid a, Guid b) => a.CompareTo(b) < 0 ? (a, b) : (b, a);
}
