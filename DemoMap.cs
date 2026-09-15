using AStar.Core;

namespace AStar;

/// <summary>
/// The original demo's obstacle placement, moved out of <c>Program</c> so the
/// console demo and the self-test can share one environment builder.
/// <para>
/// <b>Temporary.</b> Step 4 replaces this with <c>Generation/MapGenerator.cs</c>,
/// which shuffles all cell indices with a seeded RNG and blocks exactly
/// <c>floor(density × width × height)</c> of them — uniform, endpoint-
/// independent and reproducible from a single master seed. This version instead
/// rejection-samples individual cells and carves the endpoints out afterwards,
/// which biases obstacle statistics near the endpoints. Fine for a
/// hand-inspected demo, not fine for the published experiment.
/// </para>
/// </summary>
public static class DemoMap
{
    /// <summary>
    /// Places <paramref name="obstacleCount"/> obstacles at random, never on
    /// <paramref name="start"/> or <paramref name="goal"/>. Pass a seeded
    /// <see cref="Random"/> for a reproducible map.
    /// </summary>
    public static Grid Random(
        int width,
        int height,
        int obstacleCount,
        Random rand,
        (int x, int y) start,
        (int x, int y) goal)
    {
        int placeable = width * height - 2; // start and goal are off limits
        if (obstacleCount > placeable)
            throw new ArgumentOutOfRangeException(nameof(obstacleCount),
                $"Cannot place {obstacleCount} obstacles in {placeable} available cells.");

        var grid = new Grid(width, height);

        while (grid.BlockedCount < obstacleCount)
        {
            int x = rand.Next(width);
            int y = rand.Next(height);
            if ((x, y) == start || (x, y) == goal)
                continue;

            grid.Block(x, y); // no-op and retried if already blocked
        }

        return grid;
    }
}
