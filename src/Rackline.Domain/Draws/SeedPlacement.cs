using Rackline.Domain.Common;

namespace Rackline.Domain.Draws;

/// <summary>Standard bracket seeding: where each seed sits in a bracket of a given size.</summary>
public static class SeedPlacement
{
    /// <summary>Smallest power of two that holds <paramref name="entrants"/>.</summary>
    public static int BracketSize(int entrants)
    {
        Guard.That(entrants >= 2, "At least two entries are needed for a draw.");
        var size = 2;
        while (size < entrants)
        {
            size *= 2;
        }

        return size;
    }

    /// <summary>
    /// Seed number at each line of the bracket, top to bottom. Built so that the stronger seed
    /// always meets the weakest available seed, e.g. 8 gives 1, 8, 4, 5, 2, 7, 3, 6.
    /// </summary>
    public static IReadOnlyList<int> Order(int bracketSize)
    {
        Guard.That(bracketSize >= 1 && (bracketSize & (bracketSize - 1)) == 0, "Bracket size must be a power of two.");

        List<int> order = [1];
        while (order.Count < bracketSize)
        {
            var next = order.Count * 2 + 1;
            order = [.. order.SelectMany(seed => new[] { seed, next - seed })];
        }

        return order;
    }
}
