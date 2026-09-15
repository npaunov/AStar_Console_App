using System.Globalization;
using AStar.Algorithms;
using AStar.Core;

namespace AStar.Experiments;

/// <summary>
/// One configuration of the experiment: a grid size, an obstacle density and a
/// movement model. The movement model then determines the algorithm set, so a
/// configuration is everything the harness needs to run 30 × 3 executions.
/// </summary>
public sealed record Configuration(int Size, double Density, MovementModel Model)
{
    /// <summary>Grids are square, so width and height are the same number.</summary>
    public int Width => Size;

    public int Height => Size;

    public double DensityPercent => Density * 100.0;

    /// <summary>"500 X 500, 20 % OBSTACLES, 8-DIR" — for console headings.</summary>
    public string Label =>
        $"{Size} x {Size}, " +
        $"{DensityPercent.ToString("F0", CultureInfo.InvariantCulture)} % obstacles, " +
        $"{Model.Name}";

    /// <summary>
    /// "size500_density20_8dir" — the directory this configuration's figures go
    /// in. Filename-safe by construction: digits and underscores only, and the
    /// direction count rather than the model's display name, which carries a
    /// hyphen.
    /// </summary>
    public string Slug =>
        $"size{Size.ToString(CultureInfo.InvariantCulture)}_" +
        $"density{DensityPercent.ToString("F0", CultureInfo.InvariantCulture)}_" +
        $"{Model.DirectionCount.ToString(CultureInfo.InvariantCulture)}dir";
}

/// <summary>
/// The fixed experiment matrix: four grid sizes, five obstacle densities and
/// two movement models, plus the algorithm set each model implies. One place to
/// read the design off, so the menus, the runner and the documentation cannot
/// drift apart.
/// </summary>
public static class ExperimentMatrix
{
    /// <summary>
    /// Independently generated maps per configuration, one endpoint pair each.
    /// Thirty gives 30 statistically independent observations per combination,
    /// so no single map's quirks can dominate a configuration.
    /// </summary>
    public const int RunsPerConfiguration = 30;

    /// <summary>Dijkstra's position in <see cref="Variants"/>: it is the baseline.</summary>
    public const int BaselineIndex = 0;

    public static readonly int[] GridSizes = { 50, 100, 250, 500 };

    public static readonly double[] Densities = { 0.00, 0.10, 0.20, 0.30, 0.40 };

    public static readonly MovementModel[] Models =
    {
        MovementModel.FourDirectional,
        MovementModel.EightDirectional,
    };

    /// <summary>
    /// The heuristic that is <i>exact</i> for this movement model: Manhattan for
    /// 4-directional, octile for 8-directional. Also the centre panel of every
    /// figure.
    /// </summary>
    public static Heuristic PrimaryHeuristic(MovementModel model) =>
        model.DirectionCount == 4 ? Heuristic.Manhattan : Heuristic.Octile;

    /// <summary>
    /// The three variants compared on this movement model, in the fixed order
    /// the study reports and draws them: Dijkstra baseline, the model's primary
    /// heuristic, then Euclidean. Fresh instances, because each configuration
    /// gets its own run.
    /// </summary>
    public static IPathfinder[] Variants(MovementModel model) => new IPathfinder[]
    {
        new DijkstraPathfinder(),
        new AStarPathfinder(PrimaryHeuristic(model)),
        new AStarPathfinder(Heuristic.Euclidean),
    };

    /// <summary>
    /// "A* OCTILE", or just "DIJKSTRA" where there is no heuristic — the same
    /// label <see cref="SearchResult.Label"/> gives, but available before a
    /// search has been run.
    /// </summary>
    public static string Label(IPathfinder pathfinder) =>
        pathfinder.HeuristicName == Heuristic.Zero.Name
            ? pathfinder.Algorithm
            : $"{pathfinder.Algorithm} {pathfinder.HeuristicName}";
}
