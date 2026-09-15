namespace AStar.Core;

/// <summary>
/// An occupancy grid: one bit of state per cell, free or blocked.
/// <para>
/// Stored as a single flat <c>bool[]</c> indexed <c>y * Width + x</c>, with
/// exactly one way to ask whether a cell is free. Occupancy is all a grid
/// holds: there is no per-cell movement cost, because step costs are uniform
/// and the cost model lives entirely in <see cref="MovementModel"/>.
/// </para>
/// <para>
/// Flat indexing is also what makes 500x500 practical: the search state becomes
/// arrays indexed by cell number rather than
/// <c>Dictionary&lt;(int,int), …&gt;</c>.
/// </para>
/// </summary>
public sealed class Grid
{
    private readonly bool[] _blocked;

    public int Width { get; }
    public int Height { get; }

    /// <summary>Total cells, free and blocked.</summary>
    public int CellCount => _blocked.Length;

    /// <summary>Blocked cells, maintained as they are added.</summary>
    public int BlockedCount { get; private set; }

    public Grid(int width, int height)
    {
        if (width <= 0) throw new ArgumentOutOfRangeException(nameof(width));
        if (height <= 0) throw new ArgumentOutOfRangeException(nameof(height));

        Width = width;
        Height = height;
        _blocked = new bool[width * height];
    }

    /// <summary>Blocked fraction of the grid, as a percentage.</summary>
    public double DensityPercent => 100.0 * BlockedCount / CellCount;

    public int Index(int x, int y) => y * Width + x;

    public int XOf(int index) => index % Width;

    public int YOf(int index) => index / Width;

    // Single unsigned comparison per axis: negatives wrap to huge values and
    // fail the test, so this covers both bounds at once.
    public bool InBounds(int x, int y) => (uint)x < (uint)Width && (uint)y < (uint)Height;

    public bool IsBlocked(int x, int y) => _blocked[Index(x, y)];

    public bool IsFree(int x, int y) => !_blocked[Index(x, y)];

    /// <summary>Blocks a cell. Returns false if it was already blocked.</summary>
    public bool Block(int x, int y)
    {
        int index = Index(x, y);
        if (_blocked[index])
            return false;

        _blocked[index] = true;
        BlockedCount++;
        return true;
    }

    /// <summary>
    /// The obstacle mask in the <c>[y, x]</c> layout the renderers expect. Keeps
    /// <c>Rendering</c> free of any dependency on <c>Core</c>.
    /// </summary>
    public bool[,] ToMask()
    {
        var mask = new bool[Height, Width];
        for (int y = 0; y < Height; y++)
            for (int x = 0; x < Width; x++)
                mask[y, x] = _blocked[y * Width + x];
        return mask;
    }
}
