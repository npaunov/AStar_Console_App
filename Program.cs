using System.Diagnostics;
using System.Globalization;
using AStar;
using AStar.Algorithms;
using AStar.Core;
using AStar.Rendering;

/// <summary>
/// Console driver for the A* versus Dijkstra study.
/// <para>
/// Generates one environment, holds it in memory, and runs the three
/// 8-directional variants over it — Dijkstra as the baseline, A* with octile,
/// A* with euclidean — measuring each search and nothing else. In miniature,
/// this is the loop the experiment harness will run 30 times per configuration.
/// </para>
/// </summary>
class Program
{
    // Where generated artefacts go. The study's outputs live in D:\Save, outside
    // the repository, so nothing generated is ever committed by accident. The
    // repo-relative results/ directory is the fallback, so anyone cloning this
    // can still reproduce the figures without that drive.
    const string PreferredResultsRoot = @"D:\Save\results";

    // The demo environment. Still unseeded: reproducible generation arrives with
    // the seed scheme in the next step, so each invocation draws a fresh map.
    const int DemoWidth = 30;
    const int DemoHeight = 30;
    const int DemoObstacles = 300;

    /// <summary>One search plus the cost of running it. Measured by the caller.</summary>
    sealed record Measurement(SearchResult Result, double ElapsedMs, long AllocatedBytes);

    static int Main(string[] args)
    {
        var (selfTest, resultsDirectory) = ParseArguments(args);
        if (selfTest)
            return SelfTest.Run() ? 0 : 1;

        var model = MovementModel.EightDirectional;
        (int x, int y) start = (0, 0);
        (int x, int y) goal = (DemoWidth - 1, DemoHeight - 1);

        // Generated once and shared, so all three searches run on a byte-identical
        // environment with the same endpoints. Without that the comparison would
        // be meaningless.
        var grid = DemoMap.Random(DemoWidth, DemoHeight, DemoObstacles, new Random(), start, goal);
        var mask = grid.ToMask();

        // Panel and report order is fixed and documented: baseline first, then
        // the movement model's primary heuristic, then euclidean.
        IPathfinder[] variants =
        {
            new DijkstraPathfinder(),
            new AStarPathfinder(Heuristic.Octile),
            new AStarPathfinder(Heuristic.Euclidean),
        };

        var measured = new Measurement[variants.Length];
        for (int i = 0; i < variants.Length; i++)
            measured[i] = Measure(variants[i], grid, start, goal, model);

        Report(grid, mask, start, goal, model, measured);
        CrossCheckCosts(measured);

        string figurePath = WriteFigure(grid, mask, start, goal, model, measured, resultsDirectory);
        Console.WriteLine($"Figure written to: {figurePath}");
        return 0;
    }

    /// <summary>
    /// Runs one search with the clock and the allocation counter wrapped around
    /// it, and nothing else inside them.
    /// </summary>
    static Measurement Measure(
        IPathfinder pathfinder, Grid grid, (int x, int y) start, (int x, int y) goal, MovementModel model)
    {
        // Clear the heap before every search, so a collection provoked by the
        // previous one cannot land inside this one's timing. The per-search work
        // arrays are all far over the 85 KB large-object threshold, and
        // large-object allocation is what triggers generation-2 collections — a
        // pause that would bias the algorithms unevenly, since they allocate at
        // different rates.
        GC.Collect();
        GC.WaitForPendingFinalizers();
        GC.Collect();

        var sw = new Stopwatch();
        long allocatedBefore = GC.GetTotalAllocatedBytes(precise: true);
        sw.Start();
        var result = pathfinder.Search(grid, start, goal, model);
        sw.Stop();
        long allocatedBytes = GC.GetTotalAllocatedBytes(precise: true) - allocatedBefore;

        return new Measurement(result, sw.Elapsed.TotalMilliseconds, allocatedBytes);
    }

    static void Report(
        Grid grid,
        bool[,] mask,
        (int x, int y) start,
        (int x, int y) goal,
        MovementModel model,
        Measurement[] measured)
    {
        Console.WriteLine($"Grid {grid.Width} x {grid.Height}, {grid.BlockedCount} obstacles " +
                          $"({grid.DensityPercent.ToString("F1", CultureInfo.InvariantCulture)} %), {model.Name}");
        Console.WriteLine($"Start {ConsoleGridRenderer.FormatCoord(start)}  " +
                          $"Goal {ConsoleGridRenderer.FormatCoord(goal)}");

        // The ASCII view can only show one search; show the movement model's
        // primary heuristic. The composite PNG carries all three.
        if (ConsoleGridRenderer.CanPrint(grid.Width, grid.Height))
        {
            var primary = measured[1];
            Console.WriteLine($"\nExplored set and path for {primary.Result.Label}:");
            ConsoleGridRenderer.PrintGrid(
                mask, primary.Result.Path, primary.Result.ExploredCells(grid.Width), start, goal);
        }

        Console.WriteLine();
        Console.WriteLine("ALGORITHM      EXPANDED  GENERATED  PEAK OPEN  PATH      COST   TIME (MS)   ALLOCATED");
        foreach (var m in measured)
        {
            var r = m.Result;
            string cost = r.Success ? r.PathCost.ToString("F4", CultureInfo.InvariantCulture) : "-";
            Console.WriteLine(
                $"{r.Label,-14}{r.ExpandedNodes,10}{r.GeneratedNodes,11}{r.PeakOpenSet,11}" +
                $"{(r.Success ? r.PathLengthCells.ToString(CultureInfo.InvariantCulture) : "-"),6}" +
                $"{cost,10}" +
                $"{m.ElapsedMs.ToString("F3", CultureInfo.InvariantCulture),12}" +
                $"{FormatBytes(m.AllocatedBytes),12}");
        }

        if (!measured[0].Result.Success)
            Console.WriteLine("\nNo route exists on this map — expected occasionally at this density.");
    }

    /// <summary>
    /// The optimality cross-check: every variant must agree with the Dijkstra
    /// baseline on cost. Compared with a relative epsilon, because √2
    /// accumulates rounding differently depending on how a path is assembled —
    /// exact equality would be the wrong test.
    /// </summary>
    static void CrossCheckCosts(Measurement[] measured)
    {
        const double epsilon = 1e-9;

        var baseline = measured[0].Result;
        if (!baseline.Success)
            return;

        double worst = 0;
        foreach (var m in measured)
        {
            double difference = Math.Abs(m.Result.PathCost - baseline.PathCost);
            // Relative where there is something to be relative to; a zero-cost
            // baseline only happens when start and goal coincide.
            double deviation = baseline.PathCost == 0 ? difference : difference / Math.Abs(baseline.PathCost);
            worst = Math.Max(worst, deviation);
        }

        bool pass = worst <= epsilon;
        Console.WriteLine();
        Console.ForegroundColor = pass ? ConsoleColor.Green : ConsoleColor.Red;
        Console.WriteLine($"Optimality cross-check: {(pass ? "PASS" : "FAIL")} — " +
                          $"worst relative deviation {worst.ToString("E2", CultureInfo.InvariantCulture)} " +
                          $"(tolerance {epsilon.ToString("E0", CultureInfo.InvariantCulture)})");
        Console.ResetColor();
        Console.WriteLine();
    }

    /// <summary>
    /// Renders the three searches as one side-by-side composite and returns the
    /// full path of the file it wrote.
    /// </summary>
    static string WriteFigure(
        Grid grid,
        bool[,] mask,
        (int x, int y) start,
        (int x, int y) goal,
        MovementModel model,
        Measurement[] measured,
        string resultsDirectory)
    {
        string density = grid.DensityPercent.ToString("F1", CultureInfo.InvariantCulture);

        var panels = new PanelData[measured.Length];
        for (int i = 0; i < measured.Length; i++)
        {
            var r = measured[i].Result;
            panels[i] = new PanelData
            {
                Blocked = mask,
                Start = start,
                Goal = goal,
                // Materialised here, outside the measured region.
                Explored = r.ExploredCells(grid.Width),
                Path = r.Success ? r.Path : null,
                Labels = new[]
                {
                    r.Label,
                    $"GRID: {grid.Width} X {grid.Height} {model.Name}",
                    $"EXPANDED: {r.ExpandedNodes}   PATH: {r.PathLengthCells} CELLS",
                    $"OBSTACLES: {density} %",
                    // InvariantCulture matters twice over: this machine's locale
                    // uses a comma as the decimal separator, and the bitmap font
                    // has no comma glyph, so a comma would render as a blank.
                    r.Success
                        ? $"COST: {r.PathCost.ToString("F3", CultureInfo.InvariantCulture)}"
                        : "COST: NO ROUTE",
                    $"TIME: {measured[i].ElapsedMs.ToString("F2", CultureInfo.InvariantCulture)} MS",
                    $"MEMORY: {FormatBytes(measured[i].AllocatedBytes)}",
                },
            };
        }

        var image = GridImageRenderer.RenderComposite(
            panels, GridImageRenderer.SuggestScale(grid.Width, grid.Height));

        string figurePath = Path.Combine(resultsDirectory, "demo", "astar_vs_dijkstra.png");
        PngEncoder.Write(figurePath, image);
        return figurePath;
    }

    /// <summary>
    /// Arguments: <c>--selftest</c> runs the known-answer checks; the first
    /// argument without a <c>--</c> prefix overrides the output directory. The
    /// prefix convention exists because the output directory was a bare
    /// positional argument before any flags existed.
    /// </summary>
    static (bool SelfTest, string ResultsDirectory) ParseArguments(string[] args)
    {
        bool selfTest = false;
        string? resultsDirectory = null;

        foreach (string arg in args)
        {
            if (arg.StartsWith("--", StringComparison.Ordinal))
            {
                if (arg.Equals("--selftest", StringComparison.OrdinalIgnoreCase))
                    selfTest = true;
                else
                    Console.WriteLine($"Ignoring unknown flag: {arg}");
            }
            else if (resultsDirectory is null && !string.IsNullOrWhiteSpace(arg))
            {
                resultsDirectory = arg;
            }
        }

        return (selfTest, resultsDirectory ?? DefaultResultsDirectory());
    }

    // Results never belong in bin/, so that one results directory accumulates
    // across runs. Order of preference: PreferredResultsRoot if its parent
    // exists, then results/ beside the project file, then the working directory.
    static string DefaultResultsDirectory()
    {
        string? preferredParent = Path.GetDirectoryName(PreferredResultsRoot);
        if (!string.IsNullOrEmpty(preferredParent) && Directory.Exists(preferredParent))
            return PreferredResultsRoot;

        var directory = new DirectoryInfo(AppContext.BaseDirectory);
        while (directory is not null)
        {
            if (directory.GetFiles("AStar_Console_App.csproj").Length > 0)
                return Path.Combine(directory.FullName, "results");
            directory = directory.Parent;
        }
        return Path.Combine(Directory.GetCurrentDirectory(), "results");
    }

    // Byte counts in a fixed set of units. InvariantCulture keeps the decimal
    // separator a dot: this machine's locale uses a comma, and the bitmap font
    // has no comma glyph, so a comma would render as a blank on the figure.
    static string FormatBytes(long bytes)
    {
        const double Kilobyte = 1024.0;
        const double Megabyte = Kilobyte * 1024.0;

        if (Math.Abs(bytes) >= Megabyte)
            return (bytes / Megabyte).ToString("F2", CultureInfo.InvariantCulture) + " MB";
        if (Math.Abs(bytes) >= Kilobyte)
            return (bytes / Kilobyte).ToString("F1", CultureInfo.InvariantCulture) + " KB";
        return bytes.ToString(CultureInfo.InvariantCulture) + " B";
    }
}
