namespace Rackline.Domain.Common;

/// <summary>Source of randomness injected into draws so they stay deterministic under test.</summary>
public interface IRandom
{
    /// <summary>Returns an integer in the range [0, <paramref name="maxExclusive"/>).</summary>
    int Next(int maxExclusive);
}

/// <summary>An <see cref="IRandom"/> that produces the same sequence for the same seed.</summary>
public sealed class SeededRandom(int seed) : IRandom
{
    private readonly Random _random = new(seed);

    public int Next(int maxExclusive) => _random.Next(maxExclusive);
}
