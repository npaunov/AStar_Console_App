using System.Globalization;
using AStar.Core;
using AStar.Rendering;

namespace AStar.Experiments;

/// <summary>
/// Writes one three-panel composite PNG per run — the three algorithms side by
/// side on the same map, with the same start, goal and optimal cost, exploring
/// visibly different areas.
/// <para>
/// Rendered from the <b>timed</b> run, not from a re-run: the explored set is
/// the closed-set array the search already maintains for its own guard, so
/// capturing it costs the measurement nothing.
/// </para>
/// </summary>
public sealed class FigureWriter
{
    /// <summary>Scale meaning "use the figure-scale table for this grid size".</summary>
    public const int AutoScale = 0;

    /// <summary>All of a configuration's figures live in one directory.</summary>
    private const string FiguresFolder = "figures";

    private readonly int _scale;

    public FigureWriter(string resultsDirectory, Configuration configuration, int scale = AutoScale)
    {
        if (scale != AutoScale && scale < 1) throw new ArgumentOutOfRangeException(nameof(scale));

        OutputDirectory = Path.Combine(resultsDirectory, FiguresFolder, configuration.Slug);
        _scale = scale;
    }

    /// <summary>Where this configuration's figures go; created on the first write.</summary>
    public string OutputDirectory { get; }

    public int FiguresWritten { get; private set; }

    /// <summary>
    /// Figures already in <see cref="OutputDirectory"/>. Re-running a
    /// configuration overwrites them, which is harmless at the same master seed
    /// and silent data loss at a different one — so the caller warns first.
    /// </summary>
    public int ExistingFigureCount =>
        System.IO.Directory.Exists(OutputDirectory)
            ? System.IO.Directory.GetFiles(OutputDirectory, "run*.png").Length
            : 0;

    /// <summary>
    /// Renders one run's three searches as a single composite and returns the
    /// file it wrote. Called after the clock has stopped on all three — encoding
    /// a PNG costs orders of magnitude more than the searches it draws.
    /// </summary>
    public string Write(
        int run,
        Grid grid,
        (int x, int y) start,
        (int x, int y) goal,
        MovementModel model,
        IReadOnlyList<Measurement> measured)
    {
        var panels = BuildPanels(grid, grid.ToMask(), start, goal, model, measured);
        int pxPerCell = _scale == AutoScale ? GridImageRenderer.SuggestScale(grid.Width, grid.Height) : _scale;

        // run01.png … run30.png: zero-padded so a file listing sorts in run
        // order rather than putting run 10 before run 2.
        string path = Path.Combine(OutputDirectory, $"run{run.ToString("D2", CultureInfo.InvariantCulture)}.png");
        PngEncoder.Write(path, GridImageRenderer.RenderComposite(panels, pxPerCell));

        FiguresWritten++;
        return path;
    }

    /// <summary>
    /// One panel per measured search, in the order the study fixes: Dijkstra
    /// baseline, the movement model's primary heuristic, then euclidean.
    /// <para>
    /// The six caption rows are defined here and nowhere else, so the harness
    /// and the demo cannot caption the same picture differently.
    /// </para>
    /// </summary>
    public static PanelData[] BuildPanels(
        Grid grid,
        bool[,] mask,
        (int x, int y) start,
        (int x, int y) goal,
        MovementModel model,
        IReadOnlyList<Measurement> measured)
    {
        // Counted off the drawn mask rather than taken from the requested
        // density, so a caption can never drift from its own picture.
        string density = grid.DensityPercent.ToString("F1", CultureInfo.InvariantCulture);

        var panels = new PanelData[measured.Count];
        for (int i = 0; i < measured.Count; i++)
        {
            var result = measured[i].Result;
            panels[i] = new PanelData
            {
                Blocked = mask,
                Start = start,
                Goal = goal,
                // Materialised here, outside the measured region.
                Explored = result.ExploredCells(grid.Width),
                Path = result.Success ? result.Path : null,
                Labels = new[]
                {
                    result.Label,
                    $"GRID: {grid.Width} X {grid.Height} {model.Name}",
                    $"EXPANDED: {result.ExpandedNodes}   PATH: {result.PathLengthCells} CELLS",
                    $"OBSTACLES: {density} %",
                    // InvariantCulture matters twice over: this machine's locale
                    // uses a comma as the decimal separator, and the bitmap font
                    // has no comma glyph, so a comma would render as a blank.
                    $"TIME: {measured[i].ElapsedMs.ToString("F2", CultureInfo.InvariantCulture)} MS",
                    $"MEMORY: {FormatBytes(measured[i].AllocatedBytes)}",
                },
            };
        }

        return panels;
    }

    /// <summary>
    /// Byte counts in a fixed set of units. InvariantCulture for the same two
    /// reasons the caption rows need it: the locale's decimal separator is a
    /// comma, and the bitmap font has no comma glyph.
    /// </summary>
    public static string FormatBytes(long bytes)
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
