namespace AStar.Core;

/// <summary>
/// An estimate of the remaining cost from one cell to another.
/// <para>
/// All four variants go through the same delegate indirection, so none of them
/// gains a measurement advantage from how it is dispatched — including
/// <see cref="Zero"/>, which keeps Dijkstra's accounting comparable with A*'s.
/// </para>
/// <para>
/// Admissibility, which the study has to assert: <see cref="Manhattan"/> is
/// exact on the 4-directional model and therefore admissible and consistent
/// there, but inadmissible on 8-directional (it overestimates a diagonal step
/// as 2 rather than √2). <see cref="Octile"/> is exact on 8-directional and
/// admissible on both. <see cref="Euclidean"/> is admissible and consistent on
/// both, but a weak lower bound on 4-directional — deliberately so; that
/// contrast is the point of the comparison. <see cref="Zero"/> is trivially
/// admissible and turns A* into Dijkstra.
/// </para>
/// </summary>
public sealed class Heuristic
{
    private readonly Func<int, int, int, int, double> _estimate;

    /// <summary>Label for the CSV <c>heuristic</c> column and figure captions.</summary>
    public string Name { get; }

    private Heuristic(string name, Func<int, int, int, int, double> estimate)
    {
        Name = name;
        _estimate = estimate;
    }

    public double Estimate(int fromX, int fromY, int toX, int toY) =>
        _estimate(fromX, fromY, toX, toY);

    /// <summary>|dx| + |dy|. Exact for 4-directional movement.</summary>
    public static readonly Heuristic Manhattan = new("MANHATTAN", static (ax, ay, bx, by) =>
        Math.Abs(ax - bx) + Math.Abs(ay - by));

    /// <summary>Straight-line distance. Admissible for both movement models.</summary>
    public static readonly Heuristic Euclidean = new("EUCLIDEAN", static (ax, ay, bx, by) =>
    {
        double dx = ax - bx;
        double dy = ay - by;
        return Math.Sqrt(dx * dx + dy * dy);
    });

    /// <summary>
    /// Diagonal-aware distance, exact for 8-directional movement with a √2
    /// diagonal. The expression is written exactly as the original code wrote
    /// it, so the doubles it produces are identical and the refactor stays
    /// verifiably behaviour-preserving.
    /// </summary>
    public static readonly Heuristic Octile = new("OCTILE", static (ax, ay, bx, by) =>
    {
        int dx = Math.Abs(ax - bx);
        int dy = Math.Abs(ay - by);
        double d = 1.0;             // straight step
        double d2 = Math.Sqrt(2);   // diagonal step
        return d * (dx + dy) + (d2 - 2 * d) * Math.Min(dx, dy);
    });

    /// <summary>h ≡ 0. Reduces A* to a uniform-cost search.</summary>
    public static readonly Heuristic Zero = new("NONE", static (_, _, _, _) => 0.0);
}
