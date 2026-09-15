using System.Diagnostics;
using System.Globalization;
using AStar;
using AStar.Algorithms;
using AStar.Core;
using AStar.Generation;
using AStar.Rendering;

/// <summary>
/// Console driver for the A* versus Dijkstra study.
/// <para>
/// Generates one environment from the master seed, holds it in memory, and runs
/// the three 8-directional variants over it — Dijkstra as the baseline, A* with
/// octile, A* with euclidean — measuring each search and nothing else. In
/// miniature, this is the loop the experiment harness will run 30 times per
/// configuration; <c>--seed</c> changes which environment it draws, and the same
/// seed always draws the same one.
/// </para>
/// </summary>
class Program
{
    // Where generated artefacts go. The study's outputs live in D:\Save, outside
    // the repository, so nothing generated is ever committed by accident. The
    // repo-relative results/ directory is the fallback, so anyone cloning this
    // can still reproduce the figures without that drive.
    const string PreferredResultsRoot = @"D:\Save\results";

    // The demo environment: run 1 of a configuration the real matrix contains,
    // so the demo is a miniature of the experiment rather than a special case.
    const int DemoWidth = 30;
    const int DemoHeight = 30;
    const double DemoDensity = 0.30;
    const int DemoRun = 1;

    /// <summary>One search plus the cost of running it. Measured by the caller.</summary>
    sealed record Measurement(SearchResult Result, double ElapsedMs, long AllocatedBytes);

    static int Main(string[] args)
    {
        if (!TryParseArguments(args, out var options, out string error))
        {
            Console.Error.WriteLine(error);
            return 2;
        }

        if (options.SelfTest)
            return SelfTest.Run(options.MasterSeed) ? 0 : 1;

        var model = MovementModel.EightDirectional;

        // Both seeds come from the master seed, so this whole environment — the
        // obstacles and the two endpoints — is reproducible from one integer.
        ulong mapSeed = SeedScheme.MapSeed(options.MasterSeed, DemoWidth, DemoHeight, DemoDensity, DemoRun);
        ulong endpointSeed = SeedScheme.EndpointSeed(options.MasterSeed, DemoWidth, DemoHeight, DemoDensity, DemoRun);

        // Generated once and shared, so all three searches run on a byte-identical
        // environment with the same endpoints. Without that the comparison would
        // be meaningless.
        var grid = MapGenerator.Generate(DemoWidth, DemoHeight, DemoDensity, mapSeed);

        var endpoints = EndpointSampler.Sample(grid, endpointSeed);
        if (!endpoints.Success)
        {
            Console.Error.WriteLine($"Could not place start and goal: {endpoints.Failure}");
            return 3;
        }

        var start = endpoints.Start;
        var goal = endpoints.Goal;
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

        Report(grid, mask, start, goal, model, measured, options.MasterSeed, mapSeed, endpointSeed, endpoints);
        CrossCheckCosts(measured);

        string figurePath = WriteFigure(grid, mask, start, goal, model, measured, options.ResultsDirectory);
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
        Measurement[] measured,
        long masterSeed,
        ulong mapSeed,
        ulong endpointSeed,
        EndpointPair endpoints)
    {
        Console.WriteLine($"Grid {grid.Width} x {grid.Height}, {grid.BlockedCount} obstacles " +
                          $"({grid.DensityPercent.ToString("F1", CultureInfo.InvariantCulture)} %), {model.Name}");
        // Printed so the environment can be rebuilt from the console output alone.
        Console.WriteLine($"Master seed {masterSeed}, run {DemoRun}  " +
                          $"(map seed {mapSeed:X16}, endpoint seed {endpointSeed:X16})");
        Console.WriteLine($"Start {ConsoleGridRenderer.FormatCoord(start)}  " +
                          $"Goal {ConsoleGridRenderer.FormatCoord(goal)}  " +
                          $"(at least {endpoints.MinSeparation.ToString("F1", CultureInfo.InvariantCulture)} " +
                          $"cells apart, drawn in {endpoints.Attempts} attempt(s))");

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

    sealed record Options(bool SelfTest, string ResultsDirectory, long MasterSeed);

    /// <summary>
    /// Arguments: <c>--selftest</c> runs the known-answer checks;
    /// <c>--seed N</c> or <c>--seed=N</c> sets the master seed every environment
    /// is derived from; the first argument without a <c>--</c> prefix overrides
    /// the output directory. The prefix convention exists because the output
    /// directory was a bare positional argument before any flags existed.
    /// <para>
    /// An unreadable seed is an error rather than a fallback to the default:
    /// quietly running a different seed than the one asked for is the one
    /// failure this project cannot afford.
    /// </para>
    /// </summary>
    static bool TryParseArguments(string[] args, out Options options, out string error)
    {
        bool selfTest = false;
        string? resultsDirectory = null;
        long masterSeed = SeedScheme.DefaultMasterSeed;

        options = null!;
        error = "";

        for (int i = 0; i < args.Length; i++)
        {
            string arg = args[i];

            if (!arg.StartsWith("--", StringComparison.Ordinal))
            {
                if (resultsDirectory is null && !string.IsNullOrWhiteSpace(arg))
                    resultsDirectory = arg;
                continue;
            }

            if (arg.Equals("--selftest", StringComparison.OrdinalIgnoreCase))
            {
                selfTest = true;
                continue;
            }

            // Both "--seed=N" and "--seed N", because either is the obvious one
            // to type and the wrong guess would otherwise be read as a directory.
            string? seedText = null;
            if (arg.StartsWith("--seed=", StringComparison.OrdinalIgnoreCase))
                seedText = arg["--seed=".Length..];
            else if (arg.Equals("--seed", StringComparison.OrdinalIgnoreCase) && i + 1 < args.Length)
                seedText = args[++i];

            if (seedText is not null)
            {
                if (!long.TryParse(seedText, NumberStyles.Integer, CultureInfo.InvariantCulture, out masterSeed))
                {
                    error = $"Not a valid master seed: '{seedText}'. Expected a whole number.";
                    return false;
                }
                continue;
            }

            if (arg.Equals("--seed", StringComparison.OrdinalIgnoreCase))
            {
                error = "--seed needs a value, for example --seed 20260915.";
                return false;
            }

            Console.WriteLine($"Ignoring unknown flag: {arg}");
        }

        options = new Options(selfTest, resultsDirectory ?? DefaultResultsDirectory(), masterSeed);
        return true;
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
