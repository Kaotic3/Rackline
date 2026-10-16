using Rackline.Domain.Common;
using Rackline.Domain.Tournaments;

namespace Rackline.Domain.Draws;

public static class SeedingOrder
{
    /// <summary>
    /// Orders entries for a draw: seeded entries by seed, then unseeded entries shuffled.
    /// The result is indexed so that position 0 is treated as seed 1.
    /// </summary>
    public static IReadOnlyList<Guid> Arrange(IEnumerable<Entry> entries, IRandom random)
    {
        var all = entries.ToList();
        var seeded = all.Where(e => e.Seed is not null).OrderBy(e => e.Seed).Select(e => e.Id);
        var unseeded = all.Where(e => e.Seed is null).Select(e => e.Id).ToList();

        // Fisher–Yates shuffle.
        for (var i = unseeded.Count - 1; i > 0; i--)
        {
            var j = random.Next(i + 1);
            (unseeded[i], unseeded[j]) = (unseeded[j], unseeded[i]);
        }

        return [.. seeded, .. unseeded];
    }
}
