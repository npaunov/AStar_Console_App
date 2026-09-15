namespace AStar.Core;

/// <summary>
/// Which neighbours a cell has, and what each step costs.
/// <para>
/// <b>Diagonals are permissive.</b> The only legality test is "destination in
/// bounds and free" — corner cutting is allowed, and so is the zero-width
/// squeeze between two obstacles that touch corner to corner. That keeps the
/// 8-directional model exactly 8-connectivity (Moore), which is why the
/// percolation thresholds quoted in the study hold as textbook constants and
/// why octile is the exact remaining cost on an obstacle-free grid. The
/// consequence is that a diagonal chain of obstacles is not a barrier, so a
/// rendered path can appear to cross one. This is a deliberate, recorded
/// decision — do not add a corner-cut rule.
/// </para>
/// </summary>
public sealed class MovementModel
{
    /// <summary>
    /// Written the same way the original code wrote it, so the diagonal step
    /// cost is the identical double and paths stay bit-for-bit comparable.
    /// </summary>
    private static readonly double Diagonal = Math.Sqrt(2);

    private readonly int[] _offsetX;
    private readonly int[] _offsetY;
    private readonly double[] _stepCost;

    /// <summary>Short label for CSV rows and figure captions.</summary>
    public string Name { get; }

    public int DirectionCount => _offsetX.Length;

    private MovementModel(string name, int[] offsetX, int[] offsetY, double[] stepCost)
    {
        Name = name;
        _offsetX = offsetX;
        _offsetY = offsetY;
        _stepCost = stepCost;
    }

    /// <summary>N, E, S, W — every step costs 1.</summary>
    public static readonly MovementModel FourDirectional = new(
        "4-DIR",
        new[] { 0, 1, 0, -1 },
        new[] { -1, 0, 1, 0 },
        new[] { 1.0, 1.0, 1.0, 1.0 });

    /// <summary>
    /// N, NE, E, SE, S, SW, W, NW — orthogonal steps cost 1, diagonals √2.
    /// Direction order is the original code's, kept for fidelity; it cannot
    /// affect results, because the open-set comparer is a total order.
    /// </summary>
    public static readonly MovementModel EightDirectional = new(
        "8-DIR",
        new[] { 0, 1, 1, 1, 0, -1, -1, -1 },
        new[] { -1, -1, 0, 1, 1, 1, 0, -1 },
        new[] { 1.0, Diagonal, 1.0, Diagonal, 1.0, Diagonal, 1.0, Diagonal });

    public int OffsetX(int direction) => _offsetX[direction];

    public int OffsetY(int direction) => _offsetY[direction];

    public double StepCost(int direction) => _stepCost[direction];
}
