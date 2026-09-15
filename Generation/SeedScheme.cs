namespace AStar.Generation;

/// <summary>
/// Derives every environment's seed from one master seed, so the whole study is
/// reproducible from a single integer. The mixer is written out rather than
/// taken from the BCL because <c>GetHashCode</c> and <c>HashCode.Combine</c> are
/// randomised per process, which would destroy that silently.
/// </summary>
public static class SeedScheme
{
    /// <summary>Master seed used unless one is given on the command line.</summary>
    public const long DefaultMasterSeed = 20260915;

    /// <summary>2^64 / golden ratio, odd — the standard splitmix64 increment.</summary>
    private const ulong Golden = 0x9E3779B97F4A7C15UL;

    /// <summary>Separates the endpoint seed from the map seed of the same run.</summary>
    private const long EndpointTag = 0x5EED;

    /// <summary>Seed for one run's obstacle layout.</summary>
    public static ulong MapSeed(long masterSeed, int width, int height, double density, int run) =>
        Derive(masterSeed, width, height, DensityKey(density), run);

    /// <summary>Seed for one run's start/goal pair: the map seed's inputs plus a tag.</summary>
    public static ulong EndpointSeed(long masterSeed, int width, int height, double density, int run) =>
        Derive(masterSeed, width, height, DensityKey(density), run, EndpointTag);

    /// <summary>
    /// Folds the fields into one 64-bit seed, order-sensitively. The master seed
    /// is mixed before the loop so an all-zero input cannot yield a zero seed.
    /// </summary>
    public static ulong Derive(long masterSeed, params long[] fields)
    {
        ulong hash = Mix(unchecked((ulong)masterSeed + Golden));
        foreach (long field in fields)
            hash = Mix(unchecked(hash + Golden + (ulong)field));
        return hash;
    }

    /// <summary>
    /// The splitmix64 finaliser: a bijection on 64 bits with full avalanche, so
    /// seeds differing by one give unrelated streams. Quoted verbatim in
    /// methodology.md, so keep it readable as a formula.
    /// </summary>
    public static ulong Mix(ulong z)
    {
        unchecked
        {
            z = (z ^ (z >> 30)) * 0xBF58476D1CE4E5B9UL;
            z = (z ^ (z >> 27)) * 0x94D049BB133111EBUL;
            return z ^ (z >> 31);
        }
    }

    /// <summary>
    /// A density as an exact key: its IEEE 754 bits. Rounding to a percentage
    /// would make 33.3 % and 33.0 % share a seed.
    /// </summary>
    private static long DensityKey(double density) => BitConverter.DoubleToInt64Bits(density);
}

/// <summary>
/// The splitmix64 generator — a counter stepped by an odd constant and run
/// through <see cref="SeedScheme.Mix"/> — used instead of <see cref="Random"/>,
/// whose algorithm Microsoft does not guarantee across major .NET versions. The
/// output only looks random: one seed always replays exactly the same sequence.
/// A class, not a struct, because a copied struct would silently repeat its
/// draws.
/// </summary>
public sealed class SplitMix64
{
    private const ulong Golden = 0x9E3779B97F4A7C15UL;

    private ulong _state;

    public SplitMix64(ulong seed) => _state = seed;

    public ulong NextUInt64()
    {
        unchecked { _state += Golden; }
        return SeedScheme.Mix(_state);
    }

    /// <summary>
    /// A uniform integer in <c>[0, exclusiveBound)</c>. The top of the 64-bit
    /// range is rejected and redrawn so there is no modulo bias.
    /// </summary>
    public int Next(int exclusiveBound)
    {
        if (exclusiveBound <= 0)
            throw new ArgumentOutOfRangeException(nameof(exclusiveBound), "Bound must be positive.");

        ulong bound = (ulong)exclusiveBound;
        ulong threshold = unchecked((ulong)-(long)bound) % bound;   // 2^64 mod bound

        ulong draw;
        do
        {
            draw = NextUInt64();
        }
        while (draw < threshold);

        return (int)(draw % bound);
    }
}
