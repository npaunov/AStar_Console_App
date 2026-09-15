using System.Globalization;
using AStar.Core;

namespace AStar.Generation;

/// <summary>The sampled start/goal pair, or the reason no valid pair could be drawn.</summary>
public sealed record EndpointPair
{
    public required bool Success { get; init; }
    public required (int x, int y) Start { get; init; }
    public required (int x, int y) Goal { get; init; }

    /// <summary>Draws made, including those rejected for being too close.</summary>
    public required int Attempts { get; init; }

    /// <summary>The separation the pair had to beat, in cells.</summary>
    public required double MinSeparation { get; init; }

    /// <summary>Why sampling failed; null on success.</summary>
    public string? Failure { get; init; }
}

/// <summary>
/// Draws start and goal uniformly from the <i>free</i> cells of a map, rejecting
/// pairs closer than half the grid diagonal. Without that rule most routes would
/// be short on every grid size, confounding grid size with route length — which
/// is precisely the effect the study sets out to measure.
/// </summary>
public static class EndpointSampler
{
    /// <summary>Minimum separation as a fraction of the grid diagonal.</summary>
    public const double DefaultMinSeparationFraction = 0.5;

    /// <summary>
    /// Draws allowed before giving up. Rejection sampling can only run out when
    /// almost no pair satisfies the rule, i.e. on a map too fragmented for the
    /// experiment — which is reported rather than silently worked around.
    /// </summary>
    public const int DefaultMaxAttempts = 10_000;

    /// <summary>
    /// Samples a pair. The same seed and map always give the same pair; the
    /// endpoints are not checked for connectivity, because whether a route
    /// exists is a result, not a precondition.
    /// </summary>
    public static EndpointPair Sample(
        Grid grid,
        ulong seed,
        double minSeparationFraction = DefaultMinSeparationFraction,
        int maxAttempts = DefaultMaxAttempts)
    {
        if (minSeparationFraction < 0.0)
            throw new ArgumentOutOfRangeException(nameof(minSeparationFraction));
        if (maxAttempts <= 0)
            throw new ArgumentOutOfRangeException(nameof(maxAttempts));

        double diagonal = Math.Sqrt((double)grid.Width * grid.Width + (double)grid.Height * grid.Height);
        double minSeparation = minSeparationFraction * diagonal;

        // Drawing from the free cells directly keeps the draw uniform over them
        // without burning attempts on obstacles, which at 40 % density would be
        // two draws in five.
        int[] free = FreeCells(grid);
        if (free.Length < 2)
            return Failed(0, minSeparation,
                $"the map has {free.Length} free cells, so no pair can be drawn");

        var rng = new SplitMix64(seed);
        for (int attempt = 1; attempt <= maxAttempts; attempt++)
        {
            int a = free[rng.Next(free.Length)];
            int b = free[rng.Next(free.Length)];
            if (a == b)
                continue;

            (int x, int y) start = (a % grid.Width, a / grid.Width);
            (int x, int y) goal = (b % grid.Width, b / grid.Width);

            double dx = start.x - goal.x;
            double dy = start.y - goal.y;
            if (Math.Sqrt(dx * dx + dy * dy) < minSeparation)
                continue;

            return new EndpointPair
            {
                Success = true,
                Start = start,
                Goal = goal,
                Attempts = attempt,
                MinSeparation = minSeparation,
            };
        }

        return Failed(maxAttempts, minSeparation,
            $"no two free cells at least {minSeparation.ToString("F1", CultureInfo.InvariantCulture)} " +
            $"cells apart were drawn in {maxAttempts} attempts ({free.Length} free cells)");
    }

    private static int[] FreeCells(Grid grid)
    {
        var free = new int[grid.CellCount - grid.BlockedCount];
        int next = 0;
        for (int y = 0; y < grid.Height; y++)
            for (int x = 0; x < grid.Width; x++)
                if (grid.IsFree(x, y))
                    free[next++] = grid.Index(x, y);
        return free;
    }

    private static EndpointPair Failed(int attempts, double minSeparation, string reason) =>
        new()
        {
            Success = false,
            Start = (0, 0),
            Goal = (0, 0),
            Attempts = attempts,
            MinSeparation = minSeparation,
            Failure = reason,
        };
}
