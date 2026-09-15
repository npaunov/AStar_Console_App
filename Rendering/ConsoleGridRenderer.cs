namespace AStar.Rendering;

/// <summary>
/// The ASCII grid view, for small demo grids only, called once at the end of a
/// run rather than once per node expansion.
/// <para>
/// Takes the same <c>bool[y, x]</c> obstacle mask the image renderer takes, so
/// nothing here depends on the search internals, and neither width nor height
/// is threaded through the signature — both come off the mask. Column headers
/// are numeric: the original <c>(char)('A' + x)</c> labels ran past 'Z' at 26
/// columns and printed the 30-wide demo grid's goal as "^30".
/// </para>
/// </summary>
public static class ConsoleGridRenderer
{
    /// <summary>Largest grid still legible (and quick) as ASCII in a console window.</summary>
    public const int MaxPrintableWidth = 60;
    public const int MaxPrintableCells = 3600;

    /// <summary>True when the grid is small enough to be worth printing as text.</summary>
    public static bool CanPrint(int width, int height) =>
        width <= MaxPrintableWidth && width * height <= MaxPrintableCells;

    /// <summary>Formats a cell as "(x,y)" — coordinates instead of spreadsheet-style markers.</summary>
    public static string FormatCoord((int x, int y) coord) => $"({coord.x},{coord.y})";

    /// <summary>
    /// Prints obstacles, the explored set, the path and the endpoints. Both
    /// <paramref name="path"/> and <paramref name="explored"/> are optional.
    /// Free cells are left blank so that the obstacles and the searched region
    /// are what the eye picks up.
    /// </summary>
    public static void PrintGrid(
        bool[,] blocked,
        IEnumerable<(int x, int y)>? path,
        IEnumerable<(int x, int y)>? explored,
        (int x, int y) start,
        (int x, int y) goal)
    {
        int height = blocked.GetLength(0);
        int width = blocked.GetLength(1);

        // Hashed once up front; the original re-scanned the whole path list for
        // every cell of every repaint.
        var pathCells = path is null ? null : new HashSet<(int, int)>(path);
        var exploredCells = explored is null ? null : new HashSet<(int, int)>(explored);

        PrintColumnHeader(width);

        for (int y = 0; y < height; y++)
        {
            Console.Write($"{y,3} "); // Row label, 0-based to match the coordinates
            for (int x = 0; x < width; x++)
            {
                if ((x, y) == start)
                    WriteCell("S ", ConsoleColor.Green);
                else if ((x, y) == goal)
                    WriteCell("G ", ConsoleColor.Yellow);
                else if (blocked[y, x])
                    WriteCell("@ ", ConsoleColor.Red);
                else if (pathCells is not null && pathCells.Contains((x, y)))
                    WriteCell("* ", ConsoleColor.Red);
                else if (exploredCells is not null && exploredCells.Contains((x, y)))
                    WriteCell(". ", ConsoleColor.DarkCyan);
                else
                    Console.Write("  ");
            }
            Console.WriteLine();
        }
    }

    /// <summary>
    /// Two header rows — tens digit above units — so the labels stay two
    /// characters wide and keep working for any grid width.
    /// </summary>
    private static void PrintColumnHeader(int width)
    {
        Console.Write("    ");
        for (int x = 0; x < width; x++)
            Console.Write(x < 10 ? "  " : $"{x / 10 % 10} ");
        Console.WriteLine();

        Console.Write("    ");
        for (int x = 0; x < width; x++)
            Console.Write($"{x % 10} ");
        Console.WriteLine();
    }

    /// <summary>Prints the path as a coordinate sequence, truncated if it is long.</summary>
    public static void PrintPathSequence(IReadOnlyList<(int x, int y)> path, int maxSteps = 200)
    {
        int shown = Math.Min(path.Count, maxSteps);
        for (int i = 0; i < shown; i++)
        {
            Console.Write(FormatCoord(path[i]));
            if (i < shown - 1)
                Console.Write(" -> ");
        }
        if (shown < path.Count)
            Console.Write($" -> ... ({path.Count - shown} more)");
        Console.WriteLine();
    }

    private static void WriteCell(string text, ConsoleColor colour)
    {
        Console.ForegroundColor = colour;
        Console.Write(text);
        Console.ResetColor();
    }
}
