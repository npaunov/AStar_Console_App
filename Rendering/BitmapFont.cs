namespace AStar.Rendering;

/// <summary>
/// A hardcoded 5x7 bitmap font, so figure panels can carry their own labels
/// (algorithm, heuristic, expanded-node count) without pulling in a text or
/// imaging library. Supported characters: A-Z, 0-9, ':', '*', '%', '.', '-'
/// and space. Lower case is folded to upper case; anything else renders blank.
/// </summary>
public static class BitmapFont
{
    public const int GlyphWidth = 5;
    public const int GlyphHeight = 7;

    /// <summary>Horizontal gap between glyphs, in unscaled pixels.</summary>
    public const int Tracking = 1;

    // Each glyph is 7 rows of 5 pixels, read left to right, top to bottom.
    private static readonly Dictionary<char, string> Glyphs = new()
    {
        ['A'] = ".###.#...##...#######...##...##...#",
        ['B'] = "####.#...##...#####.#...##...#####.",
        ['C'] = ".###.#...##....#....#....#...#.###.",
        ['D'] = "####.#...##...##...##...##...#####.",
        ['E'] = "######....#....####.#....#....#####",
        ['F'] = "######....#....####.#....#....#....",
        ['G'] = ".###.#...##....#..###...##...#.###.",
        ['H'] = "#...##...##...#######...##...##...#",
        ['I'] = "#####..#....#....#....#....#..#####",
        ['J'] = "..###...#....#....#....#.#..#..##..",
        ['K'] = "#...##..#.#.#..##...#.#..#..#.#...#",
        ['L'] = "#....#....#....#....#....#....#####",
        ['M'] = "#...###.###.#.##...##...##...##...#",
        ['N'] = "#...###..##.#.##..###...##...##...#",
        ['O'] = ".###.#...##...##...##...##...#.###.",
        ['P'] = "####.#...##...#####.#....#....#....",
        ['Q'] = ".###.#...##...##...##.#.#.###.....#",
        ['R'] = "####.#...##...#####.#.#..#..#.#...#",
        ['S'] = ".#####....#.....###.....#....#####.",
        ['T'] = "#####..#....#....#....#....#....#..",
        ['U'] = "#...##...##...##...##...##...#.###.",
        ['V'] = "#...##...##...##...##...#.#.#...#..",
        ['W'] = "#...##...##...##.#.##.#.###.###...#",
        ['X'] = "#...##...#.#.#...#...#.#.#...##...#",
        ['Y'] = "#...##...#.#.#...#....#....#....#..",
        ['Z'] = "#####....#...#...#...#...#....#####",
        ['0'] = ".###.#...##..###.#.###..##...#.###.",
        ['1'] = "..#...##....#....#....#....#...###.",
        ['2'] = ".###.#...#....#...#...#...#...#####",
        ['3'] = "#####...#...#.....#.....##...#.###.",
        ['4'] = "...#...##..#.#.#..#.#####...#....#.",
        ['5'] = "######....####.....#....##...#.###.",
        ['6'] = "..##..#...#....####.#...##...#.###.",
        ['7'] = "#####....#...#...#...#....#....#...",
        ['8'] = ".###.#...##...#.###.#...##...#.###.",
        ['9'] = ".###.#...##...#.####....#...#..##..",
        [':'] = ".......#....#.........#....#.......",
        ['*'] = ".....#.#.#.###.#####.###.#.#.#.....",
        ['%'] = "##..###.#....#...#...#....#.###..##",
        ['.'] = "..........................##...##..",
        ['-'] = "...............#####...............",
        [' '] = "...................................",
    };

    /// <summary>Width in pixels that <paramref name="text"/> occupies at the given scale.</summary>
    public static int MeasureWidth(string text, int scale) =>
        text.Length == 0 ? 0 : (text.Length * (GlyphWidth + Tracking) - Tracking) * scale;

    /// <summary>Height in pixels of a single line of text at the given scale.</summary>
    public static int MeasureHeight(int scale) => GlyphHeight * scale;

    /// <summary>
    /// Blits <paramref name="text"/> into <paramref name="image"/> with its
    /// top-left corner at (<paramref name="x"/>, <paramref name="y"/>), painting
    /// set pixels with <paramref name="colourIndex"/> and leaving unset pixels
    /// untouched. Anything falling outside the image is clipped.
    /// </summary>
    public static void DrawText(IndexedImage image, int x, int y, string text, byte colourIndex, int scale = 1)
    {
        if (scale < 1) throw new ArgumentOutOfRangeException(nameof(scale));

        int penX = x;
        foreach (char raw in text)
        {
            char c = char.ToUpperInvariant(raw);
            if (Glyphs.TryGetValue(c, out string? glyph))
                DrawGlyph(image, penX, y, glyph, colourIndex, scale);

            penX += (GlyphWidth + Tracking) * scale;
        }
    }

    private static void DrawGlyph(IndexedImage image, int x, int y, string glyph, byte colourIndex, int scale)
    {
        for (int row = 0; row < GlyphHeight; row++)
            for (int col = 0; col < GlyphWidth; col++)
            {
                if (glyph[row * GlyphWidth + col] != '#')
                    continue;

                image.FillRect(x + col * scale, y + row * scale, scale, scale, colourIndex);
            }
    }
}
