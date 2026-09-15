using System.Diagnostics;
using System.Globalization;
using AStar.Core;
using AStar.Generation;

namespace AStar.Experiments;

/// <summary>One search plus what it cost to run. Measured by <see cref="ExperimentRunner.Measure"/>.</summary>
public sealed record Measurement(SearchResult Result, double ElapsedMs, long AllocatedBytes);

/// <summary>What one invocation of the harness produced, for the exit code and the log.</summary>
public sealed record InvocationSummary
{
    public required int RowsWritten { get; init; }
    public required int EndpointFailures { get; init; }
    public required bool CrossCheckPassed { get; init; }
    public required double WorstCostDeviation { get; init; }
}

/// <summary>
/// Runs one configuration: 30 independently generated maps, one start/goal pair
/// each, three algorithms per pair, 90 measured executions and 90 CSV rows.
/// <para>
/// The map and the endpoint pair are generated once per run and shared by all
/// three variants, so the comparison is on a byte-identical problem instance —
/// which is what the reviewer asked for and what makes the optimality
/// cross-check meaningful.
/// </para>
/// </summary>
public sealed class ExperimentRunner
{
    /// <summary>
    /// Warmup executions per variant, before anything is measured.
    /// <para>
    /// One is not enough. .NET first emits an unoptimised tier-0 version of a
    /// method and recompiles it fully optimised only after roughly <b>30</b>
    /// calls, so at exactly 30 measured runs per configuration that threshold
    /// would fall <i>inside</i> the measurement set — and each algorithm would
    /// cross it at a different run, producing a step change in the timings that
    /// looks like a result and is not. Warming past the threshold puts every
    /// variant in fully optimised code before run 1. The csproj additionally
    /// disables tiered compilation, which removes the mechanism altogether; this
    /// loop also covers plain first-call JIT, which no build setting removes.
    /// </para>
    /// </summary>
    public const int WarmupExecutions = 32;

    /// <summary>
    /// The throwaway warmup map is this size regardless of the configuration.
    /// Tier promotion counts <i>calls</i>, not cells, so a small map reaches
    /// fully optimised code just as well and keeps warmup under a second even
    /// when the configuration is 500 × 500.
    /// </summary>
    private const int WarmupSize = 50;

    /// <summary>
    /// Run index 0, which the measured runs (1..30) never use, so the warmup map
    /// is drawn from the same seed scheme without ever being one of the maps
    /// under measurement.
    /// </summary>
    private const int WarmupRun = 0;

    /// <summary>Relative tolerance for the optimality cross-check (caveat 5.4).</summary>
    public const double CostEpsilon = 1e-9;

    private readonly Configuration _configuration;
    private readonly long _masterSeed;
    private readonly bool _warmup;

    public ExperimentRunner(Configuration configuration, long masterSeed, bool warmup = true)
    {
        _configuration = configuration;
        _masterSeed = masterSeed;
        _warmup = warmup;
    }

    /// <summary>
    /// Runs one search with the clock and the allocation counter wrapped around
    /// it, and nothing else inside them.
    /// <para>
    /// Also used by the single-map demo, so there is exactly one definition of
    /// the measured region in the program — the one the methodology describes.
    /// </para>
    /// </summary>
    public static Measurement Measure(
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

        // The stopwatch is constructed before the first snapshot so its own
        // allocation is not charged to the search, and both snapshots sit outside
        // the timed region because precise: true is not free.
        var sw = new Stopwatch();
        long allocatedBefore = GC.GetTotalAllocatedBytes(precise: true);
        sw.Start();
        var result = pathfinder.Search(grid, start, goal, model);
        sw.Stop();
        long allocatedBytes = GC.GetTotalAllocatedBytes(precise: true) - allocatedBefore;

        return new Measurement(result, sw.Elapsed.TotalMilliseconds, allocatedBytes);
    }

    /// <summary>
    /// Executes the configuration, writing one row per execution as it goes and
    /// printing progress. Returns what the caller needs for the exit code.
    /// </summary>
    public InvocationSummary Run(CsvRecorder recorder)
    {
        var model = _configuration.Model;
        var variants = ExperimentMatrix.Variants(model);
        var tallies = new Tally[variants.Length];
        for (int i = 0; i < tallies.Length; i++)
            tallies[i] = new Tally();

        PrintHeading(variants);

        if (_warmup)
            RunWarmup(variants);
        else
            Warn("Warmup skipped: execution_time_ms in this invocation includes JIT compilation " +
                 "and must not be read as a measurement.");

        double worstDeviation = 0;
        int existenceDisagreements = 0;
        int costMismatches = 0;
        int endpointFailures = 0;

        Console.WriteLine();
        Console.WriteLine("RUN  START         GOAL                COST  EXPANDED (BASELINE / PRIMARY / EUCLIDEAN)  CHECK");

        for (int run = 1; run <= ExperimentMatrix.RunsPerConfiguration; run++)
        {
            ulong mapSeed = SeedScheme.MapSeed(_masterSeed, _configuration.Width, _configuration.Height,
                _configuration.Density, run);
            ulong pairSeed = SeedScheme.EndpointSeed(_masterSeed, _configuration.Width, _configuration.Height,
                _configuration.Density, run);

            var grid = MapGenerator.Generate(_configuration.Width, _configuration.Height,
                _configuration.Density, mapSeed);
            var endpoints = EndpointSampler.Sample(grid, pairSeed);

            if (!endpoints.Success)
            {
                // Recorded, not skipped: a map too fragmented to hold a valid
                // pair is itself a result at these densities (caveat 5.1).
                endpointFailures++;
                foreach (var pathfinder in variants)
                    recorder.Write(UnrunRecord(pathfinder, run, mapSeed, pairSeed));

                Console.WriteLine($"{run,3}  no endpoint pair could be drawn: {endpoints.Failure}");
                continue;
            }

            var start = endpoints.Start;
            var goal = endpoints.Goal;

            var measured = new Measurement[variants.Length];
            for (int i = 0; i < variants.Length; i++)
            {
                measured[i] = Measure(variants[i], grid, start, goal, model);
                tallies[i].Add(measured[i]);
            }

            var baseline = measured[ExperimentMatrix.BaselineIndex].Result;
            bool runAgrees = true;

            for (int i = 0; i < variants.Length; i++)
            {
                var result = measured[i].Result;
                bool comparable = baseline.Success && result.Success;
                double? deviation = comparable ? Deviation(baseline.PathCost, result.PathCost) : null;

                // Agreement on cost where both found a route, agreement on
                // existence where either did not.
                bool match = comparable
                    ? deviation <= CostEpsilon
                    : baseline.Success == result.Success;

                if (comparable)
                    worstDeviation = Math.Max(worstDeviation, deviation!.Value);
                if (!match)
                {
                    runAgrees = false;
                    if (comparable) costMismatches++;
                    else existenceDisagreements++;
                }

                recorder.Write(MeasuredRecord(
                    variants[i], run, mapSeed, pairSeed, start, goal, measured[i], match, deviation));
            }

            PrintRunLine(run, start, goal, baseline, measured, runAgrees);
        }

        bool passed = costMismatches == 0 && existenceDisagreements == 0;
        PrintSummary(variants, tallies, passed, worstDeviation, existenceDisagreements, endpointFailures);

        return new InvocationSummary
        {
            RowsWritten = recorder.RowsWritten,
            EndpointFailures = endpointFailures,
            CrossCheckPassed = passed,
            WorstCostDeviation = worstDeviation,
        };
    }

    // ------------------------------------------------------------- warmup

    /// <summary>
    /// Executes every variant <see cref="WarmupExecutions"/> times on a throwaway
    /// map and discards the results, so no measured run is the first call to
    /// anything. The map is generated at the configuration's density and movement
    /// model so the warmup exercises the same code paths.
    /// </summary>
    private void RunWarmup(IPathfinder[] variants)
    {
        var grid = MapGenerator.Generate(WarmupSize, WarmupSize, _configuration.Density,
            SeedScheme.MapSeed(_masterSeed, WarmupSize, WarmupSize, _configuration.Density, WarmupRun));

        var endpoints = EndpointSampler.Sample(grid,
            SeedScheme.EndpointSeed(_masterSeed, WarmupSize, WarmupSize, _configuration.Density, WarmupRun));

        (int x, int y) start;
        (int x, int y) goal;
        if (endpoints.Success)
        {
            start = endpoints.Start;
            goal = endpoints.Goal;
        }
        else
        {
            // The warmup map is discarded anyway, so an empty grid corner to
            // corner is a perfectly good substitute — and always valid.
            grid = new Grid(WarmupSize, WarmupSize);
            start = (0, 0);
            goal = (WarmupSize - 1, WarmupSize - 1);
        }

        var sw = Stopwatch.StartNew();
        for (int i = 0; i < WarmupExecutions; i++)
            foreach (var pathfinder in variants)
                Measure(pathfinder, grid, start, goal, _configuration.Model);
        sw.Stop();

        Console.WriteLine(
            $"Warmup: {WarmupExecutions} executions of each variant on a throwaway " +
            $"{WarmupSize} x {WarmupSize} map, discarded " +
            $"({sw.Elapsed.TotalSeconds.ToString("F1", CultureInfo.InvariantCulture)} s).");
    }

    // ------------------------------------------------------------- records

    private RunRecord MeasuredRecord(
        IPathfinder pathfinder,
        int run,
        ulong mapSeed,
        ulong pairSeed,
        (int x, int y) start,
        (int x, int y) goal,
        Measurement measurement,
        bool match,
        double? deviation)
    {
        var result = measurement.Result;
        double dx = start.x - goal.x;
        double dy = start.y - goal.y;

        return new RunRecord
        {
            MasterSeed = _masterSeed,
            GridSize = _configuration.Size,
            ObstacleDensity = _configuration.Density,
            MovementModel = _configuration.Model.Name,
            Algorithm = pathfinder.Algorithm,
            Heuristic = pathfinder.HeuristicName,
            Run = run,
            MapSeed = mapSeed,
            PairSeed = pairSeed,
            Start = start,
            Goal = goal,
            StraightLineDistance = Math.Sqrt(dx * dx + dy * dy),
            ManhattanDistance = Math.Abs(start.x - goal.x) + Math.Abs(start.y - goal.y),
            // NaN is not a cost: when there is no route the column is empty.
            PathCost = result.Success ? result.PathCost : null,
            PathLengthCells = result.PathLengthCells,
            ExpandedNodes = result.ExpandedNodes,
            GeneratedNodes = result.GeneratedNodes,
            PeakOpenSet = result.PeakOpenSet,
            ExecutionTimeMs = measurement.ElapsedMs,
            AllocatedBytes = measurement.AllocatedBytes,
            Success = result.Success,
            OptimalCostMatch = match,
            CostDeviation = deviation,
        };
    }

    /// <summary>
    /// The row for a run that never happened because no valid endpoint pair could
    /// be drawn. Everything downstream of the environment is empty, because
    /// nothing downstream of it was measured.
    /// </summary>
    private RunRecord UnrunRecord(IPathfinder pathfinder, int run, ulong mapSeed, ulong pairSeed) =>
        new()
        {
            MasterSeed = _masterSeed,
            GridSize = _configuration.Size,
            ObstacleDensity = _configuration.Density,
            MovementModel = _configuration.Model.Name,
            Algorithm = pathfinder.Algorithm,
            Heuristic = pathfinder.HeuristicName,
            Run = run,
            MapSeed = mapSeed,
            PairSeed = pairSeed,
            Success = false,
        };

    // ------------------------------------------------------------- reporting

    private void PrintHeading(IPathfinder[] variants)
    {
        Console.WriteLine($"Configuration: {_configuration.Label}");
        Console.WriteLine($"Master seed:   {_masterSeed.ToString(CultureInfo.InvariantCulture)}");
        Console.WriteLine($"Runs:          {ExperimentMatrix.RunsPerConfiguration} maps x " +
                          $"{variants.Length} algorithms = " +
                          $"{ExperimentMatrix.RunsPerConfiguration * variants.Length} executions");
        Console.WriteLine($"Algorithms:    {string.Join(", ", variants.Select(ExperimentMatrix.Label))}");
        Console.WriteLine($"Obstacles:     {MapGenerator.ObstacleCount(_configuration.Width, _configuration.Height, _configuration.Density)} " +
                          $"of {_configuration.Width * _configuration.Height} cells per map");
    }

    /// <summary>
    /// One line per run: the environment, the baseline's optimal cost, and the
    /// three expanded-node counts side by side — which is the study's headline
    /// comparison, visible as it happens.
    /// </summary>
    private static void PrintRunLine(
        int run,
        (int x, int y) start,
        (int x, int y) goal,
        SearchResult baseline,
        Measurement[] measured,
        bool agrees)
    {
        string cost = baseline.Success
            ? baseline.PathCost.ToString("F4", CultureInfo.InvariantCulture)
            : "no route";

        string expanded = string.Join(" / ", measured.Select(m =>
            m.Result.ExpandedNodes.ToString(CultureInfo.InvariantCulture)));

        Console.WriteLine($"{run,3}  {Coord(start),-13} {Coord(goal),-13} {cost,10}  " +
                          $"{expanded,-43}{(agrees ? "ok" : "MISMATCH")}");
    }

    private static void PrintSummary(
        IPathfinder[] variants,
        Tally[] tallies,
        bool passed,
        double worstDeviation,
        int existenceDisagreements,
        int endpointFailures)
    {
        Console.WriteLine();
        Console.WriteLine("ALGORITHM        ROUTES  MEAN EXPANDED  MEAN GENERATED  MEAN TIME (MS)  MEAN ALLOC (KB)");
        for (int i = 0; i < variants.Length; i++)
        {
            var tally = tallies[i];
            Console.WriteLine(
                $"{ExperimentMatrix.Label(variants[i]),-16}" +
                $"{$"{tally.Succeeded}/{tally.Executed}",7} " +
                $"{tally.MeanExpanded.ToString("F1", CultureInfo.InvariantCulture),14} " +
                $"{tally.MeanGenerated.ToString("F1", CultureInfo.InvariantCulture),15} " +
                $"{tally.MeanTimeMs.ToString("F3", CultureInfo.InvariantCulture),15} " +
                $"{(tally.MeanAllocatedBytes / 1024.0).ToString("F1", CultureInfo.InvariantCulture),16}");
        }
        Console.WriteLine("Means are over executed searches, including those that found no route.");

        if (endpointFailures > 0)
            Warn($"{endpointFailures} run(s) had no valid endpoint pair and were recorded with empty metrics.");

        Console.WriteLine();
        Console.ForegroundColor = passed ? ConsoleColor.Green : ConsoleColor.Red;
        Console.WriteLine($"Optimality cross-check: {(passed ? "PASS" : "FAIL")} — " +
                          $"worst relative deviation {worstDeviation.ToString("E2", CultureInfo.InvariantCulture)} " +
                          $"(tolerance {CostEpsilon.ToString("E0", CultureInfo.InvariantCulture)})");
        if (existenceDisagreements > 0)
            Console.WriteLine($"{existenceDisagreements} execution(s) disagreed with the baseline on whether a route exists.");
        Console.ResetColor();
    }

    private static void Warn(string message)
    {
        Console.ForegroundColor = ConsoleColor.Yellow;
        Console.WriteLine(message);
        Console.ResetColor();
    }

    private static string Coord((int x, int y) cell) =>
        $"({cell.x.ToString(CultureInfo.InvariantCulture)},{cell.y.ToString(CultureInfo.InvariantCulture)})";

    private static double Deviation(double baseline, double other) =>
        baseline == 0 ? Math.Abs(other) : Math.Abs(other - baseline) / Math.Abs(baseline);

    /// <summary>Running totals for one variant, for the end-of-invocation summary only.</summary>
    private sealed class Tally
    {
        public int Executed { get; private set; }
        public int Succeeded { get; private set; }

        private long _expanded;
        private long _generated;
        private double _timeMs;
        private double _allocatedBytes;

        public void Add(Measurement measurement)
        {
            Executed++;
            if (measurement.Result.Success)
                Succeeded++;

            _expanded += measurement.Result.ExpandedNodes;
            _generated += measurement.Result.GeneratedNodes;
            _timeMs += measurement.ElapsedMs;
            _allocatedBytes += measurement.AllocatedBytes;
        }

        public double MeanExpanded => Executed == 0 ? 0 : (double)_expanded / Executed;
        public double MeanGenerated => Executed == 0 ? 0 : (double)_generated / Executed;
        public double MeanTimeMs => Executed == 0 ? 0 : _timeMs / Executed;
        public double MeanAllocatedBytes => Executed == 0 ? 0 : _allocatedBytes / Executed;
    }
}
