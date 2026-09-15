using AStar.Core;

namespace AStar.Algorithms;

/// <summary>
/// A* with a pluggable heuristic.
/// <para>
/// The open set is a <see cref="SortedSet{T}"/> of
/// <c>(f, h, x, y)</c> tuples. <c>ValueTuple</c> compares its members in order,
/// so the <i>default</i> comparer already expresses the tie-break policy the
/// study depends on — f first, then lower h, then x, then y — with no custom
/// comparer to get subtly wrong. Documenting that policy matters: on an empty
/// grid with an exact heuristic every node of every optimal path shares the
/// same f, so the expanded-node count is decided entirely by how ties are
/// broken.
/// </para>
/// <para>
/// Insertion is <b>add-only</b>: when a cell's cost improves, the improved
/// entry is added and the superseded one is left in place rather than being
/// removed. Dead entries therefore accumulate and get popped later; the
/// closed-set guard discards them without counting them.
/// This costs time and memory but never correctness — every heuristic here is
/// consistent, so a cell's cost is already optimal when it is first popped, and
/// a stale entry always carries a higher f and so arrives afterwards, finding
/// nothing to improve.
/// </para>
/// </summary>
public sealed class AStarPathfinder : IPathfinder
{
    private readonly Heuristic _heuristic;

    public AStarPathfinder(Heuristic heuristic) => _heuristic = heuristic;

    public string Algorithm => "A*";

    public string HeuristicName => _heuristic.Name;

    public SearchResult Search(Grid grid, (int x, int y) start, (int x, int y) goal, MovementModel model)
    {
        int cells = grid.CellCount;

        var open = new SortedSet<(double f, double h, int x, int y)>();

        // Flat per-cell state, replacing Dictionary<(int,int), …>. Unreached
        // cells carry an infinite cost, which makes "first time seen" and
        // "found a cheaper route" the same comparison.
        var costFromStart = new double[cells];
        Array.Fill(costFromStart, double.PositiveInfinity);
        var cameFrom = new int[cells];
        Array.Fill(cameFrom, -1);
        var closed = new bool[cells];

        int expanded = 0;
        int generated = 0;
        int peakOpen = 0;

        int startIndex = grid.Index(start.x, start.y);
        int goalIndex = grid.Index(goal.x, goal.y);

        costFromStart[startIndex] = 0.0;
        double startEstimate = _heuristic.Estimate(start.x, start.y, goal.x, goal.y);
        open.Add((startEstimate, startEstimate, start.x, start.y));
        generated++;
        peakOpen = 1;

        while (open.Count > 0)
        {
            var current = open.Min;   // O(log n) — read once, then remove
            open.Remove(current);

            int currentIndex = grid.Index(current.x, current.y);

            // Closed-set guard. A cell already expanded means this is one of the
            // superseded entries described above: discard it, and do not count
            // it, so "expanded" stays one-per-cell by construction rather than
            // by relying on a set to de-duplicate afterwards.
            if (closed[currentIndex])
                continue;

            closed[currentIndex] = true;
            expanded++;

            // Goal test on pop, not on generate: only a popped node is known to
            // have its final cost.
            if (currentIndex == goalIndex)
                return Found(grid, cameFrom, goalIndex, costFromStart[goalIndex],
                    expanded, generated, peakOpen, closed, model);

            double currentCost = costFromStart[currentIndex];

            for (int direction = 0; direction < model.DirectionCount; direction++)
            {
                int nx = current.x + model.OffsetX(direction);
                int ny = current.y + model.OffsetY(direction);

                // The whole legality test: in bounds and free. Diagonals are
                // permissive by design — see MovementModel.
                if (!grid.InBounds(nx, ny) || grid.IsBlocked(nx, ny))
                    continue;

                int neighborIndex = grid.Index(nx, ny);
                double tentativeCost = currentCost + model.StepCost(direction);
                if (tentativeCost >= costFromStart[neighborIndex])
                    continue;

                cameFrom[neighborIndex] = currentIndex;
                costFromStart[neighborIndex] = tentativeCost;

                double estimate = _heuristic.Estimate(nx, ny, goal.x, goal.y);
                open.Add((tentativeCost + estimate, estimate, nx, ny));
                generated++;

                if (open.Count > peakOpen)
                    peakOpen = open.Count;
            }
        }

        return NotFound(expanded, generated, peakOpen, closed, model);
    }

    private SearchResult Found(
        Grid grid, int[] cameFrom, int goalIndex, double cost,
        int expanded, int generated, int peakOpen, bool[] closed, MovementModel model) =>
        new()
        {
            Algorithm = Algorithm,
            HeuristicName = HeuristicName,
            MovementModelName = model.Name,
            Success = true,
            Path = SearchResult.BuildPath(cameFrom, goalIndex, grid.Width),
            PathCost = cost,
            ExpandedNodes = expanded,
            GeneratedNodes = generated,
            PeakOpenSet = peakOpen,
            ClosedMask = closed,
        };

    private SearchResult NotFound(
        int expanded, int generated, int peakOpen, bool[] closed, MovementModel model) =>
        new()
        {
            Algorithm = Algorithm,
            HeuristicName = HeuristicName,
            MovementModelName = model.Name,
            Success = false,
            Path = Array.Empty<(int x, int y)>(),
            PathCost = double.NaN,
            ExpandedNodes = expanded,
            GeneratedNodes = generated,
            PeakOpenSet = peakOpen,
            ClosedMask = closed,
        };
}
