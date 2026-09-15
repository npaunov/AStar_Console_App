namespace AStar.Core;

/// <summary>
/// Everything one search produces.
/// <para>
/// Deliberately carries <b>no execution time and no memory figure</b>. Those
/// belong to whoever wraps the <see cref="IPathfinder.Search"/> call, which is
/// what makes "measure the search and nothing else" a property of the structure
/// rather than a convention someone has to remember. The same reason
/// <see cref="ClosedMask"/> is handed over raw: turning it into a coordinate
/// list allocates, so the caller does that after stopping the clock, via
/// <see cref="ExploredCells"/>.
/// </para>
/// </summary>
public sealed class SearchResult
{
    public required string Algorithm { get; init; }
    public required string HeuristicName { get; init; }
    public required string MovementModelName { get; init; }

    public required bool Success { get; init; }

    /// <summary>Start to goal inclusive; empty when no route exists.</summary>
    public required IReadOnlyList<(int x, int y)> Path { get; init; }

    /// <summary>Total cost of <see cref="Path"/>; <c>NaN</c> when no route exists.</summary>
    public required double PathCost { get; init; }

    /// <summary>Cells in the path, the reviewer's "path length".</summary>
    public int PathLengthCells => Path.Count;

    /// <summary>Cells popped and processed. Stale re-pops are not counted.</summary>
    public required int ExpandedNodes { get; init; }

    /// <summary>Entries pushed onto the open set.</summary>
    public required int GeneratedNodes { get; init; }

    /// <summary>
    /// Largest the open set ever grew. Because the open set is add-only,
    /// superseded entries are never removed, so this counts <i>accumulated
    /// entries</i> rather than the size of the search frontier. It must be
    /// described that way in the methodology.
    /// </summary>
    public required int PeakOpenSet { get; init; }

    /// <summary>
    /// One flag per cell, indexed <c>y * Width + x</c>: true where the cell was
    /// expanded. The closed set and the explored set are the same thing, so
    /// capturing this costs the search nothing.
    /// </summary>
    public required bool[] ClosedMask { get; init; }

    /// <summary>
    /// Materialises <see cref="ClosedMask"/> as coordinates for rendering. Call
    /// this outside any measured region — it allocates.
    /// </summary>
    public List<(int x, int y)> ExploredCells(int width)
    {
        var cells = new List<(int x, int y)>(ExpandedNodes);
        for (int i = 0; i < ClosedMask.Length; i++)
            if (ClosedMask[i])
                cells.Add((i % width, i / width));
        return cells;
    }

    /// <summary>
    /// Walks the parent array back from the goal and returns the path in
    /// start-to-goal order. Shared by both pathfinders so that one part of them
    /// at least cannot drift apart.
    /// </summary>
    internal static List<(int x, int y)> BuildPath(int[] cameFrom, int goalIndex, int width)
    {
        var path = new List<(int x, int y)>();
        for (int i = goalIndex; i != -1; i = cameFrom[i])
            path.Add((i % width, i / width));
        path.Reverse();
        return path;
    }

    /// <summary>"A* OCTILE", or just "DIJKSTRA" where there is no heuristic.</summary>
    public string Label => HeuristicName == Heuristic.Zero.Name
        ? Algorithm
        : $"{Algorithm} {HeuristicName}";
}
