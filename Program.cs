using System.Diagnostics;
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
/// 30 independently generated environments against three algorithms, appends 90
/// rows to <c>runs.csv</c>, writes 30 three-panel composite figures and
/// refreshes the <c>runs_excel.csv</c> viewing copy and the
/// <c>environment.md</c> technical report beside it. <c>--demo</c>
/// runs a single-map walkthrough instead, which prints the grid and renders
/// one three-panel figure; <c>--selftest</c> runs the known-answer checks.
/// </para>
/// </summary>
class Program
{
    // Where generated artefacts go: a Results folder beside the project file,
    // so a run leaves its data next to the code that produced it and the path
    // carries no machine dependency. The folder is git-ignored, so nothing
    // generated is ever committed; methodology.md is the one exception, being
    // written by hand and belonging with the data it describes.
    const string ResultsFolderName = "Results";

    /// <summary>Marks the project root when resolving <see cref="ResultsFolderName"/>.</summary>
    const string ProjectFileName = "AStar_Console_App.csproj";

    /// <summary>One file spanning every configuration, as the study requires.</summary>
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

        // Refreshing the viewing copy and probing the machine both run no
        // experiment, so they come before anything that would prompt.
        if (options.ExcelOnly)
            return RefreshExcelViews(options.ResultsDirectory);

        if (options.EnvironmentOnly)
            return WriteEnvironmentReport(options.ResultsDirectory, options.MasterSeed);

        return options.Demo ? RunDemo(options) : RunHarness(options);
    }

    /// <summary>
    /// Writes the machine-generated technical report. Nothing is measured, so it
    /// can be regenerated at any time — but it records the build configuration
    /// it ran from, so it belongs to the build that produced the data.
    /// </summary>
    static int WriteEnvironmentReport(string resultsDirectory, long masterSeed)
    {
        string path;
        try
        {
            path = EnvironmentProbe.Write(resultsDirectory, masterSeed);
        }
        catch (Exception exception) when (
            exception is IOException or UnauthorizedAccessException)
        {
            Console.Error.WriteLine(exception.Message);
            return OutputFailed;
        }

        Console.WriteLine($"Environment report written to {path}");
        return Ok;
    }

    /// <summary>
    /// Rebuilds every spreadsheet view on disk — one per configuration
    /// directory, plus the spanning pair at the root — without running an
    /// experiment. How a results tree that predates the feature gets a view.
    /// </summary>
    static int RefreshExcelViews(string resultsDirectory)
    {
        int refreshed = 0;

        foreach (var configuration in ExperimentMatrix.All())
        {
            string csvPath = Path.Combine(configuration.DirectoryIn(resultsDirectory), RunsFileName);
            if (!File.Exists(csvPath))
                continue;

            int code = WriteExcelCopyOf(csvPath, quiet: true);
            if (code != Ok)
                return code;
            refreshed++;
        }

        if (refreshed == 0)
        {
            Console.Error.WriteLine(
                $"No {RunsFileName} found in any configuration directory under {resultsDirectory} " +
                $"— run a configuration first.");
            return OutputFailed;
        }

        Console.WriteLine($"{refreshed} per-configuration spreadsheet view(s) rewritten.");
        return WriteCombinedView(resultsDirectory, ExperimentMatrix.All());
    }

    /// <summary>
    /// Rewrites the spreadsheet viewing copy beside one <c>runs.csv</c>.
    /// <paramref name="quiet"/> suppresses the note during a batch, where it
    /// would otherwise repeat once per configuration.
    /// </summary>
    static int WriteExcelCopyOf(string canonicalPath, bool quiet = false)
    {
        string excelPath = Path.Combine(
            Path.GetDirectoryName(canonicalPath) ?? "", ExcelFileName);

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

        if (!quiet)
        {
            Console.WriteLine($"Spreadsheet view written to {excelPath}");
            Console.WriteLine("Semicolon-delimited with comma decimals, for viewing only — " +
                              $"{RunsFileName} stays the data file. Do not save over either from a spreadsheet.");
        }

        return Ok;
    }

    /// <summary>
    /// Rebuilds the spanning CSV pair at the results root from the
    /// per-configuration files, so one directly loadable file covers everything
    /// on disk.
    /// </summary>
    static int WriteCombinedView(string resultsDirectory, IReadOnlyList<Configuration> configurations)
    {
        string combinedPath = Path.Combine(resultsDirectory, RunsFileName);

        // Every configuration that has a file, not merely the ones just run, so
        // the spanning file still covers the whole matrix after a single
        // configuration is re-run on its own.
        var sources = ExperimentMatrix.All()
            .Select(configuration => Path.Combine(configuration.DirectoryIn(resultsDirectory), RunsFileName))
            .Where(File.Exists)
            .ToArray();

        int rows;
        try
        {
            rows = CsvRecorder.WriteCombined(sources, combinedPath);
        }
        catch (Exception exception) when (
            exception is IOException or InvalidDataException or UnauthorizedAccessException)
        {
            Console.Error.WriteLine(exception.Message);
            return OutputFailed;
        }

        Console.WriteLine();
        Console.WriteLine($"{rows.ToString("N0", CultureInfo.InvariantCulture)} rows from " +
                          $"{sources.Length} configuration(s) combined into {combinedPath}");

        return WriteExcelCopyOf(combinedPath);
    }

    // ------------------------------------------------------------- harness

    /// <summary>
    /// Asks whether to run the whole matrix or one configuration, then runs what
    /// was chosen. Fixed menus, no free text: the matrix is the experiment
    /// design and typing into it is not a feature.
    /// </summary>
    static int RunHarness(Options options)
    {
        var everything = ExperimentMatrix.All();

        Console.WriteLine("A* versus Dijkstra — experiment harness");
        Console.WriteLine("=======================================");
        Console.WriteLine($"Master seed {options.MasterSeed.ToString(CultureInfo.InvariantCulture)} " +
                          $"(override with --seed N)");

        if (!TryConfirm($"Run the full matrix — all {everything.Length} configurations", out bool full))
            return InputClosed;

        if (full)
            return RunConfigurations(options, everything);

        if (!TryChoose("Grid size", ExperimentMatrix.GridSizes,
                size => $"{size} x {size}", out int chosenSize))
            return InputClosed;

        if (!TryChoose("Obstacle density", ExperimentMatrix.Densities,
                density => $"{(density * 100).ToString("F0", CultureInfo.InvariantCulture)} %",
                out double chosenDensity))
            return InputClosed;

        if (!TryChoose("Movement model", ExperimentMatrix.Models, Describe, out var chosenModel))
            return InputClosed;

        return RunConfigurations(options, new[] { new Configuration(chosenSize, chosenDensity, chosenModel) });
    }

    /// <summary>
    /// Runs each configuration into its own directory — its CSV pair and its 30
    /// figures together — then rebuilds the spanning CSV pair and the
    /// environment report at the results root.
    /// </summary>
    static int RunConfigurations(Options options, IReadOnlyList<Configuration> configurations)
    {
        bool batch = configurations.Count > 1;
        if (batch)
            PrintBatchPlan(options, configurations);

        var started = Stopwatch.StartNew();
        int totalRows = 0;
        int totalFigures = 0;
        int totalEndpointFailures = 0;
        int configurationsFailingCrossCheck = 0;
        double worstDeviation = 0;

        for (int i = 0; i < configurations.Count; i++)
        {
            var configuration = configurations[i];
            string directory = configuration.DirectoryIn(options.ResultsDirectory);
            string csvPath = Path.Combine(directory, RunsFileName);

            Console.WriteLine();
            if (batch)
                Console.WriteLine($"--- {i + 1} of {configurations.Count} ---");
            Console.WriteLine($"Output:    {directory}");

            FigureWriter? figures = null;
            if (options.Figures)
            {
                figures = new FigureWriter(options.ResultsDirectory, configuration, options.Scale);

                // Re-running rewrites runNN.png. Identical at the same master
                // seed, and replaced at a different one — worth one line.
                int existing = figures.ExistingFigureCount;
                if (existing > 0)
                    Warn($"{existing} figure(s) already there and will be overwritten.");
            }

            InvocationSummary summary;
            try
            {
                // append: false — this file describes the same 30 runs as the
                // figures beside it, so a re-run replaces it rather than
                // doubling its rows.
                using var recorder = new CsvRecorder(csvPath, append: false);
                summary = new ExperimentRunner(configuration, options.MasterSeed, options.Warmup)
                    .Run(recorder, figures);
            }
            catch (Exception exception) when (
                exception is IOException or InvalidDataException or UnauthorizedAccessException)
            {
                Console.Error.WriteLine(exception.Message);
                return OutputFailed;
            }

            int viewCode = WriteExcelCopyOf(csvPath, quiet: batch);
            if (viewCode != Ok)
                return viewCode;

            totalRows += summary.RowsWritten;
            totalFigures += summary.FiguresWritten;
            totalEndpointFailures += summary.EndpointFailures;
            worstDeviation = Math.Max(worstDeviation, summary.WorstCostDeviation);
            if (!summary.CrossCheckPassed)
                configurationsFailingCrossCheck++;

            Console.WriteLine($"{summary.RowsWritten} rows and {summary.FiguresWritten} figures in {directory}");
        }

        started.Stop();

        // Derived from the per-configuration files that were just completed, so
        // it always covers exactly what is on disk and can never drift.
        int combinedCode = WriteCombinedView(options.ResultsDirectory, configurations);
        if (combinedCode != Ok)
            return combinedCode;

        // A technical report that has to be remembered is one that ends up
        // describing a different build from the one that wrote the rows.
        int environmentCode = WriteEnvironmentReport(options.ResultsDirectory, options.MasterSeed);
        if (environmentCode != Ok)
            return environmentCode;

        if (batch)
            PrintBatchSummary(configurations.Count, totalRows, totalFigures, totalEndpointFailures,
                configurationsFailingCrossCheck, worstDeviation, started.Elapsed);

        return configurationsFailingCrossCheck == 0 ? Ok : CrossCheckFailed;
    }

    static void PrintBatchPlan(Options options, IReadOnlyList<Configuration> configurations)
    {
        int executions = configurations.Count * ExperimentMatrix.RunsPerConfiguration * 3;

        Console.WriteLine();
        Console.WriteLine($"{configurations.Count} configurations x " +
                          $"{ExperimentMatrix.RunsPerConfiguration} maps x 3 algorithms = " +
                          $"{executions.ToString("N0", CultureInfo.InvariantCulture)} measured executions");
        Console.WriteLine($"Each configuration gets its own directory under {options.ResultsDirectory}, " +
                          $"holding {RunsFileName}, {ExcelFileName} and its " +
                          $"{ExperimentMatrix.RunsPerConfiguration} figures.");
        if (!options.Figures)
            Console.WriteLine("Figures are disabled for this run.");
    }

    static void PrintBatchSummary(
        int configurations, int rows, int figures, int endpointFailures,
        int failingCrossCheck, double worstDeviation, TimeSpan elapsed)
    {
        Console.WriteLine();
        Console.WriteLine("Full matrix complete");
        Console.WriteLine("====================");
        Console.WriteLine($"Configurations:  {configurations}");
        Console.WriteLine($"Rows:            {rows.ToString("N0", CultureInfo.InvariantCulture)}");
        Console.WriteLine($"Figures:         {figures.ToString("N0", CultureInfo.InvariantCulture)}");
        Console.WriteLine($"Elapsed:         {elapsed.TotalSeconds.ToString("F1", CultureInfo.InvariantCulture)} s");

        if (endpointFailures > 0)
            Warn($"{endpointFailures} run(s) across the matrix had no valid endpoint pair " +
                 $"and were recorded with empty metrics.");

        Console.WriteLine();
        Console.ForegroundColor = failingCrossCheck == 0 ? ConsoleColor.Green : ConsoleColor.Red;
        Console.WriteLine($"Optimality cross-check across the whole matrix: " +
                          $"{(failingCrossCheck == 0 ? "PASS" : "FAIL")} — worst relative deviation " +
                          $"{worstDeviation.ToString("E2", CultureInfo.InvariantCulture)} " +
                          $"(tolerance {ExperimentRunner.CostEpsilon.ToString("E0", CultureInfo.InvariantCulture)})");
        if (failingCrossCheck > 0)
            Console.WriteLine($"{failingCrossCheck} configuration(s) disagreed with the Dijkstra baseline.");
        Console.ResetColor();
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
    /// A yes/no question, defaulting to no on a bare Enter. Re-prompts until the
    /// answer is one of the two; returns false only when stdin closes.
    /// </summary>
    static bool TryConfirm(string question, out bool answer)
    {
        Console.WriteLine();

        while (true)
        {
            Console.Write($"{question}? [y/N]: ");
            string? line = Console.ReadLine();

            if (line is null)
            {
                Console.WriteLine();
                Console.Error.WriteLine("Input closed before a choice was made.");
                answer = false;
                return false;
            }

            string trimmed = line.Trim();

            // A bare Enter means no: the full matrix is the expensive answer and
            // should never be what a stray keystroke selects.
            if (trimmed.Length == 0
                || trimmed.Equals("n", StringComparison.OrdinalIgnoreCase)
                || trimmed.Equals("no", StringComparison.OrdinalIgnoreCase))
            {
                answer = false;
                return true;
            }

            if (trimmed.Equals("y", StringComparison.OrdinalIgnoreCase)
                || trimmed.Equals("yes", StringComparison.OrdinalIgnoreCase))
            {
                answer = true;
                return true;
            }

            Console.WriteLine("Answer y or n.");
        }
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

        string figurePath = WriteFigure(
            grid, mask, start, goal, model, measured, options.ResultsDirectory, options.Scale);
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
                $"{FigureWriter.FormatBytes(m.AllocatedBytes),12}");
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
        string resultsDirectory,
        int scale)
    {
        // Panels and captions come from the harness's own figure code, so the
        // demo cannot caption the same picture differently from the study.
        var panels = FigureWriter.BuildPanels(grid, mask, start, goal, model, measured);
        int pxPerCell = scale == FigureWriter.AutoScale
            ? GridImageRenderer.SuggestScale(grid.Width, grid.Height)
            : scale;

        var image = GridImageRenderer.RenderComposite(panels, pxPerCell);

        // A throwaway, overwritten on every demo run: the harness's figures are
        // the ones that go with the data.
        string figurePath = Path.Combine(resultsDirectory, "demo", "astar_vs_dijkstra.png");
        PngEncoder.Write(figurePath, image);
        return figurePath;
    }

    // ----------------------------------------------------------- arguments

    sealed record Options(
        bool SelfTest, bool Demo, bool Warmup, bool ExcelOnly, bool EnvironmentOnly,
        bool Figures, int Scale, string ResultsDirectory, long MasterSeed);

    /// <summary>
    /// Arguments: <c>--selftest</c> runs the known-answer checks; <c>--demo</c>
    /// runs the single-map walkthrough instead of the harness;
    /// <c>--no-warmup</c> skips the JIT warmup pass, which exists so the warmup
    /// can itself be measured against; <c>--excel-only</c> rewrites the
    /// spreadsheet viewing copy from the existing <c>runs.csv</c> without
    /// running anything, which is how it is refreshed after a run that predates
    /// it; <c>--environment</c> rewrites the <c>environment.md</c> technical
    /// report and runs nothing else; <c>--no-figures</c> runs the experiment
    /// without drawing anything;
    /// <c>--scale N</c> overrides the pixels per cell the figure-scale table
    /// would pick;
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
        bool excelOnly = false;
        bool environmentOnly = false;
        bool figures = true;
        int scale = FigureWriter.AutoScale;
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

            if (arg.Equals("--excel-only", StringComparison.OrdinalIgnoreCase))
            {
                excelOnly = true;
                continue;
            }

            if (arg.Equals("--environment", StringComparison.OrdinalIgnoreCase))
            {
                environmentOnly = true;
                continue;
            }

            if (arg.Equals("--no-figures", StringComparison.OrdinalIgnoreCase))
            {
                figures = false;
                continue;
            }

            // Same two spellings as --seed, and the same refusal to fall back:
            // a scale that was misread would silently produce a figure set at
            // the wrong size.
            string? scaleText = null;
            if (arg.StartsWith("--scale=", StringComparison.OrdinalIgnoreCase))
                scaleText = arg["--scale=".Length..];
            else if (arg.Equals("--scale", StringComparison.OrdinalIgnoreCase) && i + 1 < args.Length)
                scaleText = args[++i];

            if (scaleText is not null)
            {
                if (!int.TryParse(scaleText, NumberStyles.Integer, CultureInfo.InvariantCulture, out scale)
                    || scale < 1)
                {
                    error = $"Not a valid scale: '{scaleText}'. Expected pixels per cell, 1 or more.";
                    return false;
                }
                continue;
            }

            if (arg.Equals("--scale", StringComparison.OrdinalIgnoreCase))
            {
                error = "--scale needs a value, for example --scale 4.";
                return false;
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

        options = new Options(selfTest, demo, warmup, excelOnly, environmentOnly, figures, scale,
            resultsDirectory ?? DefaultResultsDirectory(), masterSeed);
        return true;
    }

    // Resolved by walking up from the binary to the project file, so the same
    // directory is used whether the program was started by the CLI, by an IDE
    // or from bin/ directly — results never land in bin/, and one directory
    // accumulates across runs. A published build has no project file beside it,
    // so that case falls back to the working directory.
    static string DefaultResultsDirectory()
    {
        var directory = new DirectoryInfo(AppContext.BaseDirectory);
        while (directory is not null)
        {
            if (directory.GetFiles(ProjectFileName).Length > 0)
                return Path.Combine(directory.FullName, ResultsFolderName);
            directory = directory.Parent;
        }
        return Path.Combine(Directory.GetCurrentDirectory(), ResultsFolderName);
    }

    static void Warn(string message)
    {
        Console.ForegroundColor = ConsoleColor.Yellow;
        Console.WriteLine(message);
        Console.ResetColor();
    }
}
