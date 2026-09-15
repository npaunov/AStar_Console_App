using AStar.Core;

namespace AStar.Algorithms;

/// <summary>
/// Dijkstra's algorithm as the study's baseline.
/// <para>
/// Mathematically this is A* with h ≡ 0, and it could have been expressed as
/// <c>new AStarPathfinder(Heuristic.Zero)</c>. It is a separate class on
/// purpose: the baseline should be readable as Dijkstra by anyone reviewing the
/// code, without first having to accept that a heuristic-driven search
/// degenerates into it. The cost of that choice is a duplicated loop that could
/// drift from <see cref="AStarPathfinder"/> over time; the guard is the
/// optimality cross-check, which compares every path cost the two produce and
/// fails loudly if their ordering or accounting ever diverges.
/// </para>
/// <para>
/// <b>Early exit when the goal is popped.</b> A full distance field to every
/// cell would measure a different problem and would flatter A* unfairly, so the
/// search stops the moment the goal's cost is final — the same stopping rule
/// A* uses, which is what makes the comparison fair.
/// </para>
/// <para>
/// Open set, tie-break and add-only insertion are identical to
/// <see cref="AStarPathfinder"/>. With h ≡ 0 the priority reduces to
/// <c>(cost, 0, x, y)</c>, so ties fall through to the same x-then-y rule and
/// the two algorithms remain directly comparable.
/// </para>
/// </summary>
public sealed class DijkstraPathfinder : IPathfinder
{
    public string Algorithm => "DIJKSTRA";

    public string HeuristicName => Heuristic.Zero.Name;

    public SearchResult Search(Grid grid, (int x, int y) start, (int x, int y) goal, MovementModel model)
    {
        int cells = grid.CellCount;

        var open = new SortedSet<(double f, double h, int x, int y)>();

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
        open.Add((0.0, 0.0, start.x, start.y));
        generated++;
        peakOpen = 1;

        while (open.Count > 0)
        {
            var current = open.Min;
            open.Remove(current);

            int currentIndex = grid.Index(current.x, current.y);

            if (closed[currentIndex])
                continue;   // superseded entry; discarded, not counted

            closed[currentIndex] = true;
            expanded++;

            if (currentIndex == goalIndex)
                return Result(true, grid, cameFrom, goalIndex, costFromStart[goalIndex],
                    expanded, generated, peakOpen, closed, model);

            double currentCost = costFromStart[currentIndex];

            for (int direction = 0; direction < model.DirectionCount; direction++)
            {
                int nx = current.x + model.OffsetX(direction);
                int ny = current.y + model.OffsetY(direction);

                if (!grid.InBounds(nx, ny) || grid.IsBlocked(nx, ny))
                    continue;

                int neighborIndex = grid.Index(nx, ny);
                double tentativeCost = currentCost + model.StepCost(direction);
                if (tentativeCost >= costFromStart[neighborIndex])
                    continue;

                cameFrom[neighborIndex] = currentIndex;
                costFromStart[neighborIndex] = tentativeCost;

                // h is zero, so f is the cost so far and the second key is a
                // constant — the tie-break reduces to x then y.
                open.Add((tentativeCost, 0.0, nx, ny));
                generated++;

                if (open.Count > peakOpen)
                    peakOpen = open.Count;
            }
        }

        return Result(false, grid, cameFrom, goalIndex, double.NaN,
            expanded, generated, peakOpen, closed, model);
    }

    private SearchResult Result(
        bool success, Grid grid, int[] cameFrom, int goalIndex, double cost,
        int expanded, int generated, int peakOpen, bool[] closed, MovementModel model) =>
        new()
        {
            Algorithm = Algorithm,
            HeuristicName = HeuristicName,
            MovementModelName = model.Name,
            Success = success,
            Path = success
                ? SearchResult.BuildPath(cameFrom, goalIndex, grid.Width)
                : Array.Empty<(int x, int y)>(),
            PathCost = cost,
            ExpandedNodes = expanded,
            GeneratedNodes = generated,
            PeakOpenSet = peakOpen,
            ClosedMask = closed,
        };
}
