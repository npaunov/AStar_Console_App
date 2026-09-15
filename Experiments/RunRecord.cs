namespace AStar.Experiments;

/// <summary>
/// One CSV row: one algorithm's execution on one environment, with everything
/// needed to rebuild that environment and check the result.
/// <para>
/// Nullable fields are the ones that may not exist, and they are written as
/// <i>empty</i> CSV fields rather than as a stand-in number. The rule, which the
/// methodology states: <b>a column is empty only when the quantity was not
/// measured.</b> A search that ran but found no route therefore reports real
/// expanded-node, time and allocation figures with an empty
/// <c>path_cost</c> — while a run whose endpoints could not be drawn at all is
/// empty from <c>start_x</c> onward. Writing 0 instead would be a fabricated
/// measurement, and both cases are results the study reports.
/// </para>
/// </summary>
public sealed record RunRecord
{
    /// <summary>The one integer every seed below is derived from.</summary>
    public required long MasterSeed { get; init; }

    public required int GridSize { get; init; }
    public required double ObstacleDensity { get; init; }
    public required string MovementModel { get; init; }
    public required string Algorithm { get; init; }
    public required string Heuristic { get; init; }

    /// <summary>Run index within the configuration, 1..30.</summary>
    public required int Run { get; init; }

    public required ulong MapSeed { get; init; }
    public required ulong PairSeed { get; init; }

    public (int x, int y)? Start { get; init; }
    public (int x, int y)? Goal { get; init; }

    /// <summary>Start-to-goal distances, for regressing results on route length.</summary>
    public double? StraightLineDistance { get; init; }
    public int? ManhattanDistance { get; init; }

    /// <summary>Cost of the route found; null when no route was found.</summary>
    public double? PathCost { get; init; }

    public int? PathLengthCells { get; init; }
    public int? ExpandedNodes { get; init; }
    public int? GeneratedNodes { get; init; }
    public int? PeakOpenSet { get; init; }
    public double? ExecutionTimeMs { get; init; }
    public long? AllocatedBytes { get; init; }

    /// <summary>Whether a route was found. False also when nothing could be run.</summary>
    public required bool Success { get; init; }

    /// <summary>
    /// Whether this variant agrees with the Dijkstra baseline — on cost when both
    /// found a route, on existence when either did not. Null when no search ran.
    /// </summary>
    public bool? OptimalCostMatch { get; init; }

    /// <summary>Relative cost deviation from the baseline; null when not comparable.</summary>
    public double? CostDeviation { get; init; }
}
