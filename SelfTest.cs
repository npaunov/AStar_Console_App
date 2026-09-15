using System.Globalization;
using AStar.Algorithms;
using AStar.Core;

namespace AStar;

/// <summary>
/// Hand-verified known-answer checks, plus an equivalence check against the
/// implementation that existed before the Core/Algorithms split. A
/// zero-dependency stand-in for a test framework, and something concrete the
/// write-up can cite for correctness.
/// </summary>
public static class SelfTest
{
    /// <summary>
    /// Relative tolerance for cost comparisons. Exact <c>==</c> is invalid: √2
    /// accumulates rounding differently depending on how a path is assembled.
    /// </summary>
    private const double Epsilon = 1e-9;

    /// <summary>Relaxations performed by a stale re-pop; diagnostic only.</summary>
    private static int _rePopRelaxations;
    private static int _rePopNewDiscoveries;
    private static double _worstRePopImprovement;
    private static int _mapsWithDifferentExpansion;
    private static int _mostFewerExpansions;
    private static int _mostExtraExpansions;

    private static int _passed;
    private static int _failed;

    public static bool Run()
    {
        _passed = 0;
        _failed = 0;

        Console.WriteLine("Self-test");
        Console.WriteLine("=========");

        EmptyGrid();
        SingleGapWall();
        EnclosedGoal();
        DiagonalSqueeze();
        AlgorithmsAgreeOnCost();
        MatchesPreRefactorImplementation();

        Console.WriteLine();
        Console.WriteLine($"{_passed} passed, {_failed} failed.");
        return _failed == 0;
    }

    // ---------------------------------------------------------------- cases

    /// <summary>
    /// An obstacle-free grid, where both primary heuristics are <i>exact</i>.
    /// This is caveat 5.2 made concrete: every cell on every optimal path
    /// shares the same f, so the expanded-node count is decided entirely by the
    /// tie-break. With f-ties broken on lower h, A* marches straight down an
    /// optimal path and expands only its cells — the best case. A different
    /// tie-break could expand the whole rectangle instead.
    /// </summary>
    private static void EmptyGrid()
    {
        Section("Empty 10x10 grid, corner to corner");

        var grid = new Grid(10, 10);
        (int x, int y) start = (0, 0);
        (int x, int y) goal = (9, 9);

        var octile = Search(Heuristic.Octile, grid, start, goal, MovementModel.EightDirectional);
        Check("8-dir: route found", octile.Success);
        CheckClose("8-dir: cost is 9 diagonal steps", octile.PathCost, 9 * Math.Sqrt(2));
        CheckEqual("8-dir: path cells", octile.PathLengthCells, 10);
        CheckEqual("8-dir: expands only the path", octile.ExpandedNodes, 10);

        // 4-dir Manhattan is integer-valued throughout, so there is no
        // floating-point fuzz in the f comparisons at all here.
        var manhattan = Search(Heuristic.Manhattan, grid, start, goal, MovementModel.FourDirectional);
        Check("4-dir: route found", manhattan.Success);
        CheckClose("4-dir: cost is 18 straight steps", manhattan.PathCost, 18.0);
        CheckEqual("4-dir: path cells", manhattan.PathLengthCells, 19);
        CheckEqual("4-dir: expands only the path", manhattan.ExpandedNodes, 19);
    }

    /// <summary>A wall across the grid with a single opening in it.</summary>
    private static void SingleGapWall()
    {
        Section("Wall with one gap, 4-directional");

        var grid = Parse(
            "...#...",
            "...#...",
            "...#...",
            ".......",   // the gap
            "...#...",
            "...#...",
            "...#...");

        (int x, int y) start = (0, 3);
        (int x, int y) goal = (6, 3);

        var result = Search(Heuristic.Manhattan, grid, start, goal, MovementModel.FourDirectional);
        Check("route found through the gap", result.Success);
        CheckClose("cost is a straight line", result.PathCost, 6.0);
        CheckEqual("path cells", result.PathLengthCells, 7);

        var baseline = new DijkstraPathfinder().Search(grid, start, goal, MovementModel.FourDirectional);
        CheckClose("Dijkstra agrees on cost", baseline.PathCost, result.PathCost);
    }

    /// <summary>
    /// A reachable-looking goal walled in on all eight sides. The search must
    /// report failure after exhausting the reachable region, not loop or throw.
    /// </summary>
    private static void EnclosedGoal()
    {
        Section("Fully enclosed goal, 8-directional");

        var grid = Parse(
            ".....",
            ".###.",
            ".#.#.",
            ".###.",
            ".....");

        (int x, int y) start = (0, 0);
        (int x, int y) goal = (2, 2);

        var result = Search(Heuristic.Octile, grid, start, goal, MovementModel.EightDirectional);
        Check("no route reported", !result.Success);
        Check("cost is NaN when there is no route", double.IsNaN(result.PathCost));
        Check("path is empty", result.PathLengthCells == 0);

        // The outer ring is 16 free cells; the goal itself is never reached.
        CheckEqual("exhausts the reachable region", result.ExpandedNodes, 16);
    }

    /// <summary>
    /// Two obstacles touching corner to corner, with the path crossing the same
    /// point from the other diagonal. This <b>must</b> be traversable: diagonals
    /// are permissive by decision, which keeps the 8-directional model exactly
    /// 8-connectivity. The test exists so the rule cannot be silently "fixed"
    /// in a later session — if someone adds a corner-cut check, this fails.
    /// </summary>
    private static void DiagonalSqueeze()
    {
        Section("Diagonal squeeze between two touching obstacles");

        var grid = Parse(
            ".#",
            "#.");

        (int x, int y) start = (0, 0);
        (int x, int y) goal = (1, 1);

        var squeeze = Search(Heuristic.Octile, grid, start, goal, MovementModel.EightDirectional);
        Check("8-dir: the squeeze is traversable", squeeze.Success);
        CheckClose("8-dir: it costs one diagonal step", squeeze.PathCost, Math.Sqrt(2));
        CheckEqual("8-dir: path cells", squeeze.PathLengthCells, 2);

        // The same geometry is a solid wall once diagonals are removed.
        var blocked = Search(Heuristic.Manhattan, grid, start, goal, MovementModel.FourDirectional);
        Check("4-dir: no route exists", !blocked.Success);
        CheckEqual("4-dir: only the start is reachable", blocked.ExpandedNodes, 1);
    }

    /// <summary>
    /// The optimality cross-check, on random maps: Dijkstra and every A*
    /// variant must return the same cost whenever a route exists, and A* with a
    /// real heuristic must never expand more nodes than the baseline.
    /// </summary>
    private static void AlgorithmsAgreeOnCost()
    {
        Section("Dijkstra and A* agree on cost (40 random maps, 8-dir)");

        var rand = new Random(20260915);
        var model = MovementModel.EightDirectional;
        (int x, int y) start = (0, 0);
        (int x, int y) goal = (29, 29);

        double worstDeviation = 0;
        int routesFound = 0;
        int octileNeverWorse = 0;

        for (int map = 0; map < 40; map++)
        {
            var grid = DemoMap.Random(30, 30, 300, rand, start, goal);

            var baseline = new DijkstraPathfinder().Search(grid, start, goal, model);
            var octile = Search(Heuristic.Octile, grid, start, goal, model);
            var euclidean = Search(Heuristic.Euclidean, grid, start, goal, model);

            if (baseline.Success != octile.Success || baseline.Success != euclidean.Success)
            {
                Fail($"map {map}: algorithms disagree on whether a route exists");
                return;
            }

            if (!baseline.Success)
                continue;

            routesFound++;
            worstDeviation = Math.Max(worstDeviation, Deviation(baseline.PathCost, octile.PathCost));
            worstDeviation = Math.Max(worstDeviation, Deviation(baseline.PathCost, euclidean.PathCost));

            if (octile.ExpandedNodes <= baseline.ExpandedNodes)
                octileNeverWorse++;
        }

        Check($"routes found on {routesFound} of 40 maps", routesFound > 0);
        Check($"worst cost deviation {worstDeviation.ToString("E2", CultureInfo.InvariantCulture)} within {Epsilon:E0}",
            worstDeviation <= Epsilon);
        CheckEqual("octile never expands more than Dijkstra", octileNeverWorse, routesFound);
    }

    /// <summary>
    /// Equivalence against the pre-refactor implementation, in two parts,
    /// because the answer turned out to be more interesting than expected.
    /// <para>
    /// Every metric the study records is preserved: success, expanded nodes,
    /// the explored set cell for cell, path length and path cost. The exact
    /// <i>route</i> is not, and the closed-set guard is the entire reason.
    /// </para>
    /// <para>
    /// Octile is a consistent heuristic in exact arithmetic, so a settled cell
    /// should never be improved again. In <c>double</c> it is consistent only to
    /// within rounding: two equal-cost routes accumulate g by adding √2 and 1 in
    /// different orders, so their sums differ in the last few bits. The old code
    /// treated a 7e-15 difference as a genuine improvement, re-expanded the
    /// settled cell, and propagated a new parent downstream — choosing a
    /// different member of a set of equally optimal routes on the strength of a
    /// rounding artefact. The guard cuts that propagation off.
    /// </para>
    /// <para>
    /// The reference implementation below is temporary and can be deleted once
    /// Step 4's seed scheme makes any regression reproducible another way.
    /// </para>
    /// </summary>
    private static void MatchesPreRefactorImplementation()
    {
        Section("Matches the pre-refactor implementation (40 random maps)");

        // Part 1: against the original exactly as it was — no closed-set guard.
        // Both must find an optimal route on every map. Nothing stronger is
        // asserted, because nothing stronger is true.
        Check("original, no guard: both find a route of the same optimal cost", CompareWithReference(
            closedGuard: false, exact: false));
        Console.WriteLine($"  INFO  the original re-expanded settled cells {_rePopRelaxations} times, " +
                          $"{_rePopNewDiscoveries} of them genuine discoveries, largest apparent " +
                          $"improvement {_worstRePopImprovement.ToString("E3", CultureInfo.InvariantCulture)} " +
                          $"— rounding noise, not a better route");
        Console.WriteLine($"  INFO  expanded-node counts differ on {_mapsWithDifferentExpansion} of 40 maps, " +
                          $"from {_mostFewerExpansions} to +{_mostExtraExpansions} cells: at 1e-15 the noise " +
                          $"perturbs f, which perturbs pop order. The guard makes this deterministic.");

        // Part 2: add the closed-set guard to the original and change nothing
        // else. Everything now matches to the cell, which isolates the guard as
        // the single behavioural difference the refactor introduced.
        Check("original plus the guard: identical metrics and identical routes", CompareWithReference(
            closedGuard: true, exact: true));
    }

    /// <summary>
    /// Runs the reference and the current implementation over the same 40 maps
    /// and compares them. Returns false on the first mismatch, having reported
    /// it.
    /// </summary>
    private static bool CompareWithReference(bool closedGuard, bool exact)
    {
        var rand = new Random(20260915);   // same seed, so the same 40 maps
        var model = MovementModel.EightDirectional;
        (int x, int y) start = (0, 0);
        (int x, int y) goal = (29, 29);

        _rePopRelaxations = 0;
        _rePopNewDiscoveries = 0;
        _worstRePopImprovement = 0;
        _mapsWithDifferentExpansion = 0;
        _mostFewerExpansions = 0;
        _mostExtraExpansions = 0;

        for (int map = 0; map < 40; map++)
        {
            var grid = DemoMap.Random(30, 30, 300, rand, start, goal);

            var (legacyPath, legacyExplored) = ReferenceAStar(grid, start, goal, closedGuard);
            var current = Search(Heuristic.Octile, grid, start, goal, model);

            bool legacyFound = legacyPath.Count > 0;
            if (legacyFound != current.Success)
                return Mismatch($"map {map}: success differs (was {legacyFound}, now {current.Success})");

            var nowExplored = new HashSet<(int, int)>(current.ExploredCells(grid.Width));

            if (exact)
            {
                if (legacyExplored.Count != current.ExpandedNodes)
                    return Mismatch($"map {map}: expanded differs " +
                                    $"(was {legacyExplored.Count}, now {current.ExpandedNodes})");

                // Membership, not just the count: equal counts could still hide
                // two searches that diverged and coincidentally explored as much.
                if (!nowExplored.SetEquals(legacyExplored))
                {
                    var difference = new HashSet<(int, int)>(nowExplored);
                    difference.SymmetricExceptWith(legacyExplored);
                    return Mismatch($"map {map}: explored sets differ in {difference.Count} cells");
                }
            }
            else
            {
                // No relation is claimed here, because none holds: the rounding
                // noise perturbs f, which perturbs pop order, so the two
                // searches diverge in both directions. Only recorded, not
                // asserted.
                int difference = current.ExpandedNodes - legacyExplored.Count;
                if (difference != 0)
                {
                    _mapsWithDifferentExpansion++;
                    _mostFewerExpansions = Math.Min(_mostFewerExpansions, difference);
                    _mostExtraExpansions = Math.Max(_mostExtraExpansions, difference);
                }
            }

            if (!legacyFound)
                continue;

            if (legacyPath.Count != current.Path.Count)
                return Mismatch($"map {map}: path length differs " +
                                $"(was {legacyPath.Count}, now {current.Path.Count})");

            double legacyCost = PathCost(legacyPath);
            if (Deviation(legacyCost, current.PathCost) > Epsilon)
                return Mismatch($"map {map}: path cost differs " +
                                $"(was {legacyCost.ToString("F9", CultureInfo.InvariantCulture)}, " +
                                $"now {current.PathCost.ToString("F9", CultureInfo.InvariantCulture)})");

            if (!exact)
                continue;

            for (int i = 0; i < legacyPath.Count; i++)
                if (legacyPath[i] != current.Path[i])
                    return Mismatch($"map {map}: route differs at step {i} " +
                                    $"(was {legacyPath[i]}, now {current.Path[i]})");
        }

        return true;
    }

    private static bool Mismatch(string detail)
    {
        Console.WriteLine($"        {detail}");
        return false;
    }

    // ------------------------------------------------- reference implementation

    /// <summary>
    /// The A* that shipped before the Core/Algorithms split, preserved verbatim
    /// in structure: <see cref="SortedSet{T}"/> with a hand-written
    /// <c>(f, h, x, y)</c> comparer, dictionary state, no closed-set guard, and
    /// an explored <see cref="HashSet{T}"/>. Only the grid access is adapted.
    /// </summary>
    private static (List<(int, int)> Path, HashSet<(int, int)> Explored) ReferenceAStar(
        Grid grid, (int, int) start, (int, int) goal, bool closedGuard = false)
    {
        var closed = new HashSet<(int, int)>();
        var firstPop = new HashSet<(int, int)>();
        var openSet = new SortedSet<(double, double, (int, int))>(
            Comparer<(double, double, (int, int))>.Create((a, b) =>
            {
                int cmp = a.Item1.CompareTo(b.Item1);
                if (cmp == 0) cmp = a.Item2.CompareTo(b.Item2);
                if (cmp == 0) cmp = a.Item3.Item1.CompareTo(b.Item3.Item1);
                if (cmp == 0) cmp = a.Item3.Item2.CompareTo(b.Item3.Item2);
                return cmp;
            }));

        var cameFrom = new Dictionary<(int, int), (int, int)>();
        var gScore = new Dictionary<(int, int), double> { [start] = 0 };
        var hScore = ReferenceHeuristic(start, goal);
        openSet.Add((hScore, hScore, start));
        var explored = new HashSet<(int, int)>();

        while (openSet.Count > 0)
        {
            var current = openSet.Min.Item3;
            openSet.Remove(openSet.Min);

            if (closedGuard && !closed.Add(current))
                continue;

            explored.Add(current);

            if (current == goal)
                return (ReferenceReconstruct(cameFrom, current), explored);

            bool isRePop = !firstPop.Add(current);

            foreach (var (neighbor, moveCost) in ReferenceNeighbors(grid, current))
            {
                double tentativeG = gScore[current] + moveCost;
                bool known = gScore.TryGetValue(neighbor, out double g);
                if (!known || tentativeG < g)
                {
                    if (isRePop)
                    {
                        _rePopRelaxations++;
                        if (known) _worstRePopImprovement = Math.Max(_worstRePopImprovement, g - tentativeG);
                        else _rePopNewDiscoveries++;
                    }
                    cameFrom[neighbor] = current;
                    gScore[neighbor] = tentativeG;
                    double h = ReferenceHeuristic(neighbor, goal);
                    openSet.Add((tentativeG + h, h, neighbor));
                }
            }
        }

        return (new List<(int, int)>(), explored);
    }

    private static List<((int, int), double)> ReferenceNeighbors(Grid grid, (int, int) pos)
    {
        var neighbors = new List<((int, int), double)>();
        int[] dx = { 0, 1, 1, 1, 0, -1, -1, -1 };
        int[] dy = { -1, -1, 0, 1, 1, 1, 0, -1 };
        for (int dir = 0; dir < 8; dir++)
        {
            int nx = pos.Item1 + dx[dir];
            int ny = pos.Item2 + dy[dir];
            if (grid.InBounds(nx, ny) && grid.IsFree(nx, ny))
            {
                // The original multiplied a per-cell weight of 1 by the step
                // factor; the weight array is gone, the arithmetic is not.
                double cost = 1 * ((dx[dir] != 0 && dy[dir] != 0) ? Math.Sqrt(2) : 1.0);
                neighbors.Add(((nx, ny), cost));
            }
        }
        return neighbors;
    }

    private static double ReferenceHeuristic((int, int) a, (int, int) b)
    {
        int dx = Math.Abs(a.Item1 - b.Item1);
        int dy = Math.Abs(a.Item2 - b.Item2);
        double d = 1.0;
        double d2 = Math.Sqrt(2);
        return d * (dx + dy) + (d2 - 2 * d) * Math.Min(dx, dy);
    }

    private static List<(int, int)> ReferenceReconstruct(
        Dictionary<(int, int), (int, int)> cameFrom, (int, int) current)
    {
        var path = new List<(int, int)> { current };
        while (cameFrom.TryGetValue(current, out var prev))
        {
            current = prev;
            path.Add(current);
        }
        path.Reverse();
        return path;
    }

    // ------------------------------------------------------------- plumbing

    private static SearchResult Search(
        Heuristic heuristic, Grid grid, (int x, int y) start, (int x, int y) goal, MovementModel model) =>
        new AStarPathfinder(heuristic).Search(grid, start, goal, model);

    /// <summary>Builds a grid from rows of text, where '#' marks an obstacle.</summary>
    private static Grid Parse(params string[] rows)
    {
        int height = rows.Length;
        int width = rows[0].Length;
        var grid = new Grid(width, height);

        for (int y = 0; y < height; y++)
        {
            if (rows[y].Length != width)
                throw new ArgumentException($"Row {y} is {rows[y].Length} wide, expected {width}.", nameof(rows));

            for (int x = 0; x < width; x++)
                if (rows[y][x] == '#')
                    grid.Block(x, y);
        }

        return grid;
    }

    /// <summary>
    /// Sums the step costs along a path, the same way a search accumulates them
    /// — so a tie can be told apart from a genuinely worse route.
    /// </summary>
    private static double PathCost(IReadOnlyList<(int, int)> path)
    {
        double cost = 0;
        for (int i = 1; i < path.Count; i++)
        {
            int dx = Math.Abs(path[i].Item1 - path[i - 1].Item1);
            int dy = Math.Abs(path[i].Item2 - path[i - 1].Item2);
            cost += (dx != 0 && dy != 0) ? Math.Sqrt(2) : 1.0;
        }
        return cost;
    }

    private static double Deviation(double baseline, double other) =>
        baseline == 0 ? Math.Abs(other) : Math.Abs(other - baseline) / Math.Abs(baseline);

    private static void Section(string title) => Console.WriteLine($"\n{title}");

    private static void Check(string what, bool condition)
    {
        if (condition) Pass(what);
        else Fail(what);
    }

    private static void CheckEqual(string what, int actual, int expected)
    {
        if (actual == expected) Pass($"{what} = {expected}");
        else Fail($"{what}: expected {expected}, got {actual}");
    }

    private static void CheckClose(string what, double actual, double expected)
    {
        if (Deviation(expected, actual) <= Epsilon)
            Pass($"{what} = {expected.ToString("F6", CultureInfo.InvariantCulture)}");
        else
            Fail($"{what}: expected {expected.ToString("F6", CultureInfo.InvariantCulture)}, " +
                 $"got {actual.ToString("F6", CultureInfo.InvariantCulture)}");
    }

    private static void Pass(string what)
    {
        _passed++;
        Console.WriteLine($"  PASS  {what}");
    }

    private static void Fail(string what)
    {
        _failed++;
        Console.ForegroundColor = ConsoleColor.Red;
        Console.WriteLine($"  FAIL  {what}");
        Console.ResetColor();
    }
}
