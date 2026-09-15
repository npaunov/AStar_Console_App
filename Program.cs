using System.Globalization;
using AStar;
using AStar.Core;
using AStar.Experiments;
using AStar.Generation;
using AStar.Rendering;

/// <summary>
/// Console driver for the A* versus Dijkstra study.
/// <para>
/// By default it runs the <b>experiment harness</b>: it prompts for one
/// configuration — grid size, obstacle density, movement model — then executes
/// 30 independently generated environments against three algorithms and appends
/// 90 rows to <c>runs.csv</c>. <c>--demo</c> runs the original single-map
/// walkthrough instead, which prints the grid and renders the three-panel
/// figure; <c>--selftest</c> runs the known-answer checks.
/// </para>
/// </summary>
class Program
{
    // Where generated artefacts go. The study's outputs live in D:\Save, outside
    // the repository, so nothing generated is ever committed by accident. The
    // repo-relative results/ directory is the fallback, so anyone cloning this
    // can still reproduce the figures without that drive.
    const string PreferredResultsRoot = @"D:\Save\results";

    /// <summary>One file for every configuration, as the reviewer asked.</summary>
    const string RunsFileName = "runs.csv";

    /// <summary>
    /// The viewing copy, for spreadsheets on a comma-decimal locale. Derived
    /// from <see cref="RunsFileName"/> and never read back.
    /// </summary>
    const string ExcelFileName = "runs_excel.csv";

    // The demo environment: run 1 of a configuration the real matrix contains,
    // so the demo is a miniature of the experiment rather than a special case.
    const int DemoWidth = 30;
    const int DemoHeight = 30;
    const double DemoDensity = 0.30;
    const int DemoRun = 1;

    // Exit codes. Anything non-zero means no usable data was produced, except
    // CrossCheckFailed, which means data was produced and it is wrong.
    const int Ok = 0;
    const int CrossCheckFailed = 1;
    const int BadArguments = 2;
    const int NoEndpoints = 3;
    const int InputClosed = 4;
    const int OutputFailed = 5;

    static int Main(string[] args)
    {
        if (!TryParseArguments(args, out var options, out string error))
        {
            Console.Error.WriteLine(error);
            return BadArguments;
        }

        if (options.SelfTest)
            return SelfTest.Run(options.MasterSeed) ? Ok : CrossCheckFailed;

        // Refreshing the viewing copy runs no experiment, so it comes before
        // anything that would prompt.
        if (options.ExcelOnly)
            return WriteExcelView(options.ResultsDirectory);

        return options.Demo ? RunDemo(options) : RunHarness(options);
    }

    /// <summary>
    /// Rewrites the spreadsheet viewing copy from the canonical
    /// <c>runs.csv</c>, covering every configuration accumulated so far.
    /// </summary>
    static int WriteExcelView(string resultsDirectory)
    {
        string canonicalPath = Path.Combine(resultsDirectory, RunsFileName);
        string excelPath = Path.Combine(resultsDirectory, ExcelFileName);

        try
        {
            CsvRecorder.WriteExcelCopy(canonicalPath, excelPath);
        }
        catch (Exception exception) when (
            exception is IOException or InvalidDataException or UnauthorizedAccessException)
        {
            Console.Error.WriteLine(exception.Message);
            return OutputFailed;
        }

        Console.WriteLine($"Spreadsheet view written to {excelPath}");
        Console.WriteLine("Semicolon-delimited with comma decimals, for viewing only — " +
                          $"{RunsFileName} stays the data file. Do not save over either from a spreadsheet.");
        return Ok;
    }

    // ------------------------------------------------------------- harness

    /// <summary>
    /// Prompts for one configuration and runs it. Fixed menus, no free text: the
    /// matrix is the experiment design and typing into it is not a feature.
    /// </summary>
    static int RunHarness(Options options)
    {
        Console.WriteLine("A* versus Dijkstra — experiment harness");
        Console.WriteLine("=======================================");
        Console.WriteLine($"Master seed {options.MasterSeed.ToString(CultureInfo.InvariantCulture)} " +
                          $"(override with --seed N)");

        if (!TryChoose("Grid size", ExperimentMatrix.GridSizes,
                size => $"{size} x {size}", out int chosenSize))
            return InputClosed;

        if (!TryChoose("Obstacle density", ExperimentMatrix.Densities,
                density => $"{(density * 100).ToString("F0", CultureInfo.InvariantCulture)} %",
                out double chosenDensity))
            return InputClosed;

        if (!TryChoose("Movement model", ExperimentMatrix.Models, Describe, out var chosenModel))
            return InputClosed;

        var configuration = new Configuration(chosenSize, chosenDensity, chosenModel);
        string csvPath = Path.Combine(options.ResultsDirectory, RunsFileName);

        Console.WriteLine();

        CsvRecorder recorder;
        try
        {
            recorder = new CsvRecorder(csvPath);
        }
        catch (Exception exception) when (
            exception is IOException or InvalidDataException or UnauthorizedAccessException)
        {
            Console.Error.WriteLine(exception.Message);
            return OutputFailed;
        }

        InvocationSummary summary;
        using (recorder)
        {
            Console.WriteLine($"Recording to {recorder.Path} ({(recorder.Appending ? "appending" : "new file")})");
            summary = new ExperimentRunner(configuration, options.MasterSeed, options.Warmup).Run(recorder);
        }

        Console.WriteLine();
        Console.WriteLine($"{summary.RowsWritten} rows appended to {csvPath}");

        // After the data is safely on disk and closed, never before.
        if (options.ExcelView)
        {
            int code = WriteExcelView(options.ResultsDirectory);
            if (code != Ok)
                return code;
        }

        return summary.CrossCheckPassed ? Ok : CrossCheckFailed;
    }

    /// <summary>
    /// A movement model as the menu describes it: the model, and the algorithm
    /// set it implies. Derived from <see cref="ExperimentMatrix.Variants"/> so the
    /// menu cannot promise a comparison the harness does not run.
    /// </summary>
    static string Describe(MovementModel model)
    {
        string diagonal = model.DirectionCount == 8 ? ", diagonal step costs sqrt(2)" : "";
        string algorithms = string.Join(", ", ExperimentMatrix.Variants(model).Select(ExperimentMatrix.Label));
        return $"{model.DirectionCount}-directional{diagonal} — {algorithms}";
    }

    /// <summary>
    /// Prints a numbered menu and reads a choice, re-prompting until one is
    /// valid. Returns false only when stdin closes, which is the one case the
    /// caller cannot retry.
    /// </summary>
    static bool TryChoose<T>(string title, IReadOnlyList<T> choices, Func<T, string> describe, out T chosen)
    {
        Console.WriteLine();
        Console.WriteLine($"{title}:");
        for (int i = 0; i < choices.Count; i++)
            Console.WriteLine($"  {i + 1}) {describe(choices[i])}");

        while (true)
        {
            Console.Write($"Choose [1-{choices.Count}]: ");
            string? line = Console.ReadLine();

            if (line is null)
            {
                Console.WriteLine();
                Console.Error.WriteLine("Input closed before a choice was made.");
                chosen = default!;
                return false;
            }

            if (int.TryParse(line.Trim(), NumberStyles.Integer, CultureInfo.InvariantCulture, out int choice)
                && choice >= 1 && choice <= choices.Count)
            {
                chosen = choices[choice - 1];
                return true;
            }

            Console.WriteLine($"Not one of the choices — enter a number from 1 to {choices.Count}.");
        }
    }

    // ---------------------------------------------------------------- demo

    /// <summary>
    /// Generates one environment from the master seed, holds it in memory, and
    /// runs the three 8-directional variants over it — Dijkstra as the baseline,
    /// A* with octile, A* with euclidean — measuring each search and nothing
    /// else, then renders the three-panel figure.
    /// <para>
    /// It deliberately has <b>no warmup pass</b>, so its TIME column measures JIT
    /// compilation as much as the algorithm; every other column is real. The
    /// harness is the thing to time with.
    /// </para>
    /// </summary>
    static int RunDemo(Options options)
    {
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
            return NoEndpoints;
        }

        var start = endpoints.Start;
        var goal = endpoints.Goal;
        var mask = grid.ToMask();

        // Panel and report order is fixed and documented: baseline first, then
        // the movement model's primary heuristic, then euclidean.
        var variants = ExperimentMatrix.Variants(model);

        var measured = new Measurement[variants.Length];
        for (int i = 0; i < variants.Length; i++)
            measured[i] = ExperimentRunner.Measure(variants[i], grid, start, goal, model);

        Report(grid, mask, start, goal, model, measured, options.MasterSeed, mapSeed, endpointSeed, endpoints);
        CrossCheckCosts(measured);

        string figurePath = WriteFigure(grid, mask, start, goal, model, measured, options.ResultsDirectory);
        Console.WriteLine($"Figure written to: {figurePath}");
        return Ok;
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

        bool pass = worst <= ExperimentRunner.CostEpsilon;
        Console.WriteLine();
        Console.ForegroundColor = pass ? ConsoleColor.Green : ConsoleColor.Red;
        Console.WriteLine($"Optimality cross-check: {(pass ? "PASS" : "FAIL")} — " +
                          $"worst relative deviation {worst.ToString("E2", CultureInfo.InvariantCulture)} " +
                          $"(tolerance {ExperimentRunner.CostEpsilon.ToString("E0", CultureInfo.InvariantCulture)})");
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

    // ----------------------------------------------------------- arguments

    sealed record Options(
        bool SelfTest, bool Demo, bool Warmup, bool ExcelView, bool ExcelOnly,
        string ResultsDirectory, long MasterSeed);

    /// <summary>
    /// Arguments: <c>--selftest</c> runs the known-answer checks; <c>--demo</c>
    /// runs the single-map walkthrough instead of the harness;
    /// <c>--no-warmup</c> skips the JIT warmup pass, which exists so the warmup
    /// can itself be measured against; <c>--excel</c> also writes the
    /// spreadsheet viewing copy after the run and <c>--excel-only</c> writes it
    /// from the existing <c>runs.csv</c> without running anything;
    /// <c>--seed N</c> or <c>--seed=N</c> sets the
    /// master seed every environment is derived from; the first argument without
    /// a <c>--</c> prefix overrides the output directory. The prefix convention
    /// exists because the output directory was a bare positional argument before
    /// any flags existed.
    /// <para>
    /// An unreadable seed is an error rather than a fallback to the default:
    /// quietly running a different seed than the one asked for is the one
    /// failure this project cannot afford.
    /// </para>
    /// </summary>
    static bool TryParseArguments(string[] args, out Options options, out string error)
    {
        bool selfTest = false;
        bool demo = false;
        bool warmup = true;
        bool excelView = false;
        bool excelOnly = false;
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

            if (arg.Equals("--demo", StringComparison.OrdinalIgnoreCase))
            {
                demo = true;
                continue;
            }

            if (arg.Equals("--no-warmup", StringComparison.OrdinalIgnoreCase))
            {
                warmup = false;
                continue;
            }

            if (arg.Equals("--excel", StringComparison.OrdinalIgnoreCase))
            {
                excelView = true;
                continue;
            }

            // Implies --excel: writing the copy is the whole point of the flag.
            if (arg.Equals("--excel-only", StringComparison.OrdinalIgnoreCase))
            {
                excelOnly = true;
                excelView = true;
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

        options = new Options(selfTest, demo, warmup, excelView, excelOnly,
            resultsDirectory ?? DefaultResultsDirectory(), masterSeed);
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
