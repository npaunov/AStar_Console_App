namespace AStar.Rendering;

/// <summary>An 8-bit indexed-colour pixel buffer, ready for <see cref="PngEncoder"/>.</summary>
public sealed class IndexedImage
{
    public int Width { get; }
    public int Height { get; }

    /// <summary>One palette index per pixel, row-major: <c>Pixels[y * Width + x]</c>.</summary>
    public byte[] Pixels { get; }

    public (byte R, byte G, byte B)[] Palette { get; }

    public IndexedImage(int width, int height, (byte R, byte G, byte B)[] palette, byte fill = 0)
    {
        if (width <= 0) throw new ArgumentOutOfRangeException(nameof(width));
        if (height <= 0) throw new ArgumentOutOfRangeException(nameof(height));

        Width = width;
        Height = height;
        Palette = palette;
        Pixels = new byte[width * height];
        if (fill != 0)
            Array.Fill(Pixels, fill);
    }

    /// <summary>Fills an axis-aligned rectangle, clipped to the image bounds.</summary>
    public void FillRect(int x, int y, int width, int height, byte colourIndex)
    {
        int x0 = Math.Max(x, 0);
        int y0 = Math.Max(y, 0);
        int x1 = Math.Min(x + width, Width);
        int y1 = Math.Min(y + height, Height);
        if (x1 <= x0 || y1 <= y0)
            return; // Fully clipped away

        for (int py = y0; py < y1; py++)
            Array.Fill(Pixels, colourIndex, py * Width + x0, x1 - x0);
    }

    /// <summary>Draws a one-pixel-wide rectangle outline, clipped to the image bounds.</summary>
    public void StrokeRect(int x, int y, int width, int height, byte colourIndex)
    {
        FillRect(x, y, width, 1, colourIndex);
        FillRect(x, y + height - 1, width, 1, colourIndex);
        FillRect(x, y, 1, height, colourIndex);
        FillRect(x + width - 1, y, 1, height, colourIndex);
    }
}

/// <summary>
/// Everything one figure panel needs to draw itself: the environment, the
/// search's explored set, the resulting path, the endpoints, and the caption
/// lines printed underneath (algorithm, heuristic, expanded nodes, cost).
/// </summary>
public sealed class PanelData
{
    /// <summary>Obstacle mask indexed <c>[y, x]</c>; <c>true</c> means blocked.</summary>
    public required bool[,] Blocked { get; init; }

    public required (int x, int y) Start { get; init; }
    public required (int x, int y) Goal { get; init; }

    /// <summary>Cells popped and processed by the search. May be null.</summary>
    public IReadOnlyCollection<(int x, int y)>? Explored { get; init; }

    /// <summary>The final path from start to goal. May be null or empty.</summary>
    public IReadOnlyList<(int x, int y)>? Path { get; init; }

    /// <summary>Caption lines drawn below the panel, top to bottom.</summary>
    public IReadOnlyList<string> Labels { get; init; } = Array.Empty<string>();

    public int Width => Blocked.GetLength(1);
    public int Height => Blocked.GetLength(0);
}

/// <summary>
/// Paints grids, explored sets and paths into an <see cref="IndexedImage"/> and
/// composes several panels side by side, each with a caption strip underneath.
/// Step 2 uses a single panel; the three-panel composite figure is wired up in
/// Step 6 by handing this the same method more than one <see cref="PanelData"/>.
/// </summary>
public static class GridImageRenderer
{
    // Palette indices. The colour legend is restated in results/methodology.md.
    public const byte Free = 0;
    public const byte Obstacle = 1;
    public const byte Explored = 2;
    public const byte PathColour = 3;
    public const byte Start = 4;
    public const byte Goal = 5;
    public const byte Background = 6;
    public const byte Text = 7;
    public const byte Border = 8;

    public static readonly (byte R, byte G, byte B)[] Palette =
    {
        (255, 255, 255), // Free
        ( 24,  24,  28), // Obstacle
        (150, 200, 235), // Explored
        (215,  40,  40), // Path
        ( 30, 160,  70), // Start
        (245, 150,  20), // Goal
        ( 45,  48,  55), // Background
        (245, 245, 245), // Text
        (120, 126, 136), // Border
    };

    private const int Margin = 10;
    private const int PanelGap = 10;
    private const int BorderWidth = 1;
    private const int CaptionGap = 6;
    private const int LineSpacing = 3;

    /// <summary>
    /// Pixels per cell that keeps a panel in the 500-1000 px range, per the
    /// figure-scale table in the project context. Overridable by the caller.
    /// </summary>
    public static int SuggestScale(int gridWidth, int gridHeight) => Math.Max(gridWidth, gridHeight) switch
    {
        <= 50 => 10,
        <= 100 => 6,
        <= 250 => 3,
        <= 500 => 2,
        _ => 1,
    };

    /// <summary>Renders a single panel figure.</summary>
    public static IndexedImage Render(PanelData panel, int pxPerCell) =>
        RenderComposite(new[] { panel }, pxPerCell);

    /// <summary>
    /// Renders <paramref name="panels"/> left to right on one canvas. All panels
    /// must share the same grid dimensions so the images are directly comparable.
    /// </summary>
    public static IndexedImage RenderComposite(IReadOnlyList<PanelData> panels, int pxPerCell)
    {
        if (panels.Count == 0) throw new ArgumentException("At least one panel is required.", nameof(panels));
        if (pxPerCell < 1) throw new ArgumentOutOfRangeException(nameof(pxPerCell));

        int gridWidth = panels[0].Width;
        int gridHeight = panels[0].Height;
        for (int i = 1; i < panels.Count; i++)
            if (panels[i].Width != gridWidth || panels[i].Height != gridHeight)
                throw new ArgumentException("All panels in a composite must share the same grid size.", nameof(panels));

        int panelWidth = gridWidth * pxPerCell;
        int panelHeight = gridHeight * pxPerCell;
        int textScale = Math.Clamp(panelWidth / 300, 1, 3);
        int lineHeight = BitmapFont.MeasureHeight(textScale) + LineSpacing;

        int captionLines = 0;
        int captionWidth = 0;
        foreach (var panel in panels)
        {
            captionLines = Math.Max(captionLines, panel.Labels.Count);
            foreach (string label in panel.Labels)
                captionWidth = Math.Max(captionWidth, BitmapFont.MeasureWidth(label, textScale));
        }
        int captionHeight = captionLines == 0 ? 0 : CaptionGap + captionLines * lineHeight;

        // A caption wider than its panel widens the canvas rather than being clipped.
        int columnWidth = Math.Max(panelWidth + BorderWidth * 2, captionWidth);
        int canvasWidth = Margin * 2
            + panels.Count * columnWidth
            + (panels.Count - 1) * PanelGap;
        int canvasHeight = Margin * 2 + panelHeight + BorderWidth * 2 + captionHeight;

        var image = new IndexedImage(canvasWidth, canvasHeight, Palette, Background);

        int cursorX = Margin;
        foreach (var panel in panels)
        {
            image.StrokeRect(cursorX, Margin, panelWidth + BorderWidth * 2, panelHeight + BorderWidth * 2, Border);
            DrawPanel(image, panel, cursorX + BorderWidth, Margin + BorderWidth, pxPerCell);

            int captionY = Margin + panelHeight + BorderWidth * 2 + CaptionGap;
            foreach (string label in panel.Labels)
            {
                BitmapFont.DrawText(image, cursorX + BorderWidth, captionY, label, Text, textScale);
                captionY += lineHeight;
            }

            cursorX += columnWidth + PanelGap;
        }

        return image;
    }

    /// <summary>
    /// Paints one panel at (<paramref name="originX"/>, <paramref name="originY"/>).
    /// Later layers win: obstacles, then explored cells, then the path, then the
    /// endpoints, so start and goal are always visible.
    /// </summary>
    private static void DrawPanel(IndexedImage image, PanelData panel, int originX, int originY, int pxPerCell)
    {
        image.FillRect(originX, originY, panel.Width * pxPerCell, panel.Height * pxPerCell, Free);

        for (int y = 0; y < panel.Height; y++)
            for (int x = 0; x < panel.Width; x++)
                if (panel.Blocked[y, x])
                    DrawCell(image, originX, originY, x, y, pxPerCell, Obstacle);

        if (panel.Explored is not null)
            foreach (var (x, y) in panel.Explored)
                DrawCell(image, originX, originY, x, y, pxPerCell, Explored);

        if (panel.Path is not null)
            foreach (var (x, y) in panel.Path)
                DrawCell(image, originX, originY, x, y, pxPerCell, PathColour);

        DrawCell(image, originX, originY, panel.Start.x, panel.Start.y, pxPerCell, Start);
        DrawCell(image, originX, originY, panel.Goal.x, panel.Goal.y, pxPerCell, Goal);
    }

    private static void DrawCell(IndexedImage image, int originX, int originY, int x, int y, int pxPerCell, byte colourIndex) =>
        image.FillRect(originX + x * pxPerCell, originY + y * pxPerCell, pxPerCell, pxPerCell, colourIndex);
}
