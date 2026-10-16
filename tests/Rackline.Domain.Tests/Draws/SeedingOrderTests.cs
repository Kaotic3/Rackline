using FluentAssertions;
using Rackline.Domain.Common;
using Rackline.Domain.Draws;
using Rackline.Domain.Tournaments;

namespace Rackline.Domain.Tests.Draws;

public class SeedingOrderTests
{
    private static List<Entry> Entries(params int?[] seeds)
    {
        var tournament = new Tournament(Guid.CreateVersion7(), "Club Night", Discipline.EnglishEightBall, TournamentFormat.SingleElimination, MatchLength.RaceTo(3));
        return [.. seeds.Select(seed => tournament.AddEntry(Guid.CreateVersion7(), seed))];
    }

    [Fact]
    public void Seeded_entries_come_first_in_seed_order()
    {
        var entries = Entries(null, 3, null, 1, 2, null);

        var order = SeedingOrder.Arrange(entries, new SeededRandom(5));

        order.Take(3).Should().Equal(entries[3].Id, entries[4].Id, entries[1].Id);
        order.Skip(3).Should().BeEquivalentTo([entries[0].Id, entries[2].Id, entries[5].Id]);
    }

    [Fact]
    public void Unseeded_entries_are_shuffled_deterministically()
    {
        var entries = Entries([.. Enumerable.Repeat<int?>(null, 20)]);

        var first = SeedingOrder.Arrange(entries, new SeededRandom(9));
        var again = SeedingOrder.Arrange(entries, new SeededRandom(9));
        var other = SeedingOrder.Arrange(entries, new SeededRandom(10));

        again.Should().Equal(first);
        other.Should().NotEqual(first);
        first.Should().BeEquivalentTo(entries.Select(e => e.Id));
    }
}
