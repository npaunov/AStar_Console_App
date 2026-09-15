namespace AStar.Core;

/// <summary>
/// One shortest-path algorithm. Implementations must be stateless between calls
/// so the experiment harness can reuse a single instance across all runs of a
/// configuration without any carry-over between measurements.
/// </summary>
public interface IPathfinder
{
    /// <summary>"A*" or "DIJKSTRA" — the CSV <c>algorithm</c> column.</summary>
    string Algorithm { get; }

    /// <summary>The CSV <c>heuristic</c> column; "NONE" for Dijkstra.</summary>
    string HeuristicName { get; }

    SearchResult Search(Grid grid, (int x, int y) start, (int x, int y) goal, MovementModel model);
}
