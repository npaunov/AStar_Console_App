using AStar.Core;

namespace AStar.Generation;

/// <summary>
/// The obstacle generator, and the one the paper documents: shuffle all cell
/// indices with a seeded RNG and block exactly
/// <c>floor(density × width × height)</c> of them. Uniform and
/// endpoint-independent — the map exists before start and goal are chosen, so no
/// cell is ever special-cased and the obstacle statistics are unbiased.
/// </summary>
public static class MapGenerator
{
    /// <summary>Obstacles placed on a grid of this size at this density.</summary>
    public static int ObstacleCount(int width, int height, double density) =>
        (int)Math.Floor(density * width * height);

    /// <summary>
    /// Builds one environment. The same seed always produces the same map, on
    /// any machine and any .NET version.
    /// </summary>
    public static Grid Generate(int width, int height, double density, ulong seed)
    {
        if (density < 0.0 || density > 1.0)
            throw new ArgumentOutOfRangeException(nameof(density), density, "Density must be in [0, 1].");

        var grid = new Grid(width, height);
        int cells = grid.CellCount;
        int obstacles = Math.Min(ObstacleCount(width, height, density), cells);
        if (obstacles == 0)
            return grid;

        var order = new int[cells];
        for (int i = 0; i < cells; i++)
            order[i] = i;

        // Fisher-Yates, stopped after the first `obstacles` positions. Later
        // iterations never touch earlier positions, so this is the exact prefix
        // of the full shuffle described above — same draws, same result, without
        // shuffling 250k cells to keep 50k of them.
        var rng = new SplitMix64(seed);
        for (int i = 0; i < obstacles; i++)
        {
            int j = i + rng.Next(cells - i);
            (order[i], order[j]) = (order[j], order[i]);

            int cell = order[i];
            grid.Block(cell % width, cell / width);
        }

        return grid;
    }
}
