namespace AStar.Rendering;

/// <summary>
/// The original ASCII grid view, kept for small demo grids only and now called
/// once at the end of a run instead of once per node expansion. Column headers
/// are numeric: the old <c>(char)('A' + x)</c> labels ran past 'Z' at 26
/// columns, so the 30-wide demo grid printed its goal as "^30".
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
    /// Prints the grid with cell weights, obstacles, the path and the explored
    /// set. <paramref name="path"/> and <paramref name="explored"/> are optional.
    /// </summary>
    public static void PrintGrid(
        int[,] grid,
        int[,] cellCost,
        List<(int, int)>? path,
        HashSet<(int, int)>? explored,
        (int x, int y) start,
        (int x, int y) goal,
        int width,
        int height,
        bool showPath)
    {
        // Hashed once up front; the original re-scanned the whole path list for
        // every cell of every repaint.
        var pathCells = showPath && path is not null ? new HashSet<(int, int)>(path) : null;

        PrintColumnHeader(width);

        for (int y = 0; y < height; y++)
        {
            Console.Write($"{y,3} "); // Row label (0-based, matching the coordinates)
            for (int x = 0; x < width; x++)
            {
                if ((x, y) == start)
                    WriteCell("S ", ConsoleColor.Green);
                else if ((x, y) == goal)
                    WriteCell("G ", ConsoleColor.Green);
                else if (grid[y, x] == 99)
                    WriteCell("@ ", ConsoleColor.Red);
                else if (pathCells is not null && pathCells.Contains((x, y)))
                    WriteCell("* ", ConsoleColor.Green);
                else if (explored is not null && explored.Contains((x, y)))
                    WriteCell(". ", ConsoleColor.DarkYellow);
                else
                    WriteCell($"{cellCost[y, x]} ", ConsoleColor.DarkBlue);
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
    public static void PrintPathSequence(List<(int, int)> path, int maxSteps = 200)
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
