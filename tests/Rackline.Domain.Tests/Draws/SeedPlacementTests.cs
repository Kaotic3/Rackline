using FluentAssertions;
using Rackline.Domain.Common;
using Rackline.Domain.Draws;

namespace Rackline.Domain.Tests.Draws;

public class SeedPlacementTests
{
    [Theory]
    [InlineData(2, 2)]
    [InlineData(3, 4)]
    [InlineData(4, 4)]
    [InlineData(5, 8)]
    [InlineData(17, 32)]
    [InlineData(64, 64)]
    [InlineData(65, 128)]
    [InlineData(128, 128)]
    public void BracketSize_is_the_next_power_of_two(int entrants, int expected) =>
        SeedPlacement.BracketSize(entrants).Should().Be(expected);

    [Theory]
    [InlineData(0)]
    [InlineData(1)]
    public void BracketSize_rejects_fewer_than_two_entrants(int entrants) =>
        FluentActions.Invoking(() => SeedPlacement.BracketSize(entrants)).Should().Throw<DomainException>();

    [Fact]
    public void Order_matches_the_standard_layout()
    {
        SeedPlacement.Order(2).Should().Equal(1, 2);
        SeedPlacement.Order(4).Should().Equal(1, 4, 2, 3);
        SeedPlacement.Order(8).Should().Equal(1, 8, 4, 5, 2, 7, 3, 6);
        SeedPlacement.Order(16).Should().Equal(1, 16, 8, 9, 4, 13, 5, 12, 2, 15, 7, 10, 3, 14, 6, 11);
    }

    [Theory]
    [InlineData(6)]
    [InlineData(0)]
    public void Order_rejects_sizes_that_are_not_powers_of_two(int size) =>
        FluentActions.Invoking(() => SeedPlacement.Order(size)).Should().Throw<DomainException>();

    [Theory]
    [InlineData(2)]
    [InlineData(4)]
    [InlineData(8)]
    [InlineData(16)]
    [InlineData(32)]
    [InlineData(64)]
    [InlineData(128)]
    public void Order_has_the_standard_properties(int size)
    {
        var order = SeedPlacement.Order(size);

        order.Should().BeEquivalentTo(Enumerable.Range(1, size), "every seed appears once");

        for (var line = 0; line < size; line += 2)
        {
            (order[line] + order[line + 1]).Should().Be(size + 1, "first-round opponents' seeds sum to size + 1");
        }

        var half = size / 2;
        order.ToList().IndexOf(1).Should().BeLessThan(half);
        order.ToList().IndexOf(2).Should().BeGreaterThanOrEqualTo(half, "seeds 1 and 2 are in opposite halves");

        if (size >= 4)
        {
            var quarters = Enumerable.Range(1, 4).Select(seed => order.ToList().IndexOf(seed) / (size / 4));
            quarters.Should().OnlyHaveUniqueItems("seeds 1–4 are in different quarters");
        }
    }
}
