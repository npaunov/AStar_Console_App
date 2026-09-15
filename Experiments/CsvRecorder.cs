using System.Globalization;

namespace AStar.Experiments;

/// <summary>
/// Appends run records to <c>runs.csv</c>, one row per execution.
/// <para>
/// The reviewer asked for every individual run, never just averages, and for one
/// file spanning all configurations — so the file is opened in append mode and
/// the header is written only when the file is new. Each row is flushed as it is
/// written, so an interrupted invocation still leaves valid data behind.
/// </para>
/// <para>
/// Columns are declared once, as name/formatter pairs, so the header and the row
/// are generated from the same list and cannot fall out of step.
/// </para>
/// </summary>
public sealed class CsvRecorder : IDisposable
{
    /// <summary>
    /// Round-trippable, because the reviewer may want to re-check Dijkstra
    /// against A* from the file itself, and the 1e-9 relative tolerance needs
    /// more significant digits than a fixed-point format gives at a cost of
    /// several hundred.
    /// </summary>
    private const string CostFormat = "G17";

    /// <summary>
    /// The canonical file's separators, and they are not configurable: comma
    /// between fields, dot for decimals. This is what the reviewer's statistics
    /// tools expect and what the study documents.
    /// </summary>
    private const string FieldSeparator = ",";
    private const string DecimalSeparator = ".";

    /// <summary>
    /// The viewing copy's separators, for a machine whose locale uses a comma
    /// for decimals — where the canonical file cannot be opened by
    /// double-clicking, because the list separator is a semicolon.
    /// </summary>
    private const string ExcelFieldSeparator = ";";
    private const string ExcelDecimalSeparator = ",";

    /// <summary>
    /// Every column, once: its name, whether it holds a number, and how to
    /// render it. The header, each row and the Excel view are all generated from
    /// this one list, so none of the three can fall out of step with the others.
    /// <para>
    /// <c>Numeric</c> is not cosmetic — it is what
    /// <see cref="WriteExcelCopy"/> uses to decide which fields may have their
    /// decimal separator rewritten. The two seed columns are deliberately
    /// <b>not</b> numeric: they must stay text in a spreadsheet.
    /// </para>
    /// </summary>
    private static readonly (string Name, bool Numeric, Func<RunRecord, string> Value)[] Columns =
    {
        // master_seed is not in the reviewer's column list; it is here so a row
        // stays self-describing in a file that accumulates across invocations,
        // which may not all have used the same seed.
        ("master_seed",             true,  r => Integer(r.MasterSeed)),
        ("grid_size",               true,  r => Integer(r.GridSize)),
        ("obstacle_density",        true,  r => r.ObstacleDensity.ToString("0.00", CultureInfo.InvariantCulture)),
        ("movement_model",          false, r => r.MovementModel),
        ("algorithm",               false, r => r.Algorithm),
        ("heuristic",               false, r => r.Heuristic),
        ("run",                     true,  r => Integer(r.Run)),
        // Hex with an 0x prefix so a spreadsheet reads a seed as text: bare hex
        // digits like 0000000000001E50 parse as 1E+50 and the value is silently
        // destroyed.
        ("map_seed",                false, r => Seed(r.MapSeed)),
        ("pair_seed",               false, r => Seed(r.PairSeed)),
        ("start_x",                 true,  r => Integer(r.Start?.x)),
        ("start_y",                 true,  r => Integer(r.Start?.y)),
        ("goal_x",                  true,  r => Integer(r.Goal?.x)),
        ("goal_y",                  true,  r => Integer(r.Goal?.y)),
        ("straight_line_distance",  true,  r => Real(r.StraightLineDistance, "F6")),
        ("manhattan_distance",      true,  r => Integer(r.ManhattanDistance)),
        ("path_cost",               true,  r => Real(r.PathCost, CostFormat)),
        ("path_length_cells",       true,  r => Integer(r.PathLengthCells)),
        ("expanded_nodes",          true,  r => Integer(r.ExpandedNodes)),
        ("generated_nodes",         true,  r => Integer(r.GeneratedNodes)),
        ("peak_open_set",           true,  r => Integer(r.PeakOpenSet)),
        ("execution_time_ms",       true,  r => Real(r.ExecutionTimeMs, "F6")),
        ("allocated_bytes",         true,  r => Integer(r.AllocatedBytes)),
        // Upper case because it is the one spelling Excel, R and pandas all read
        // back as a boolean rather than as text.
        ("success",                 false, r => Boolean(r.Success)),
        ("optimal_cost_match",      false, r => Boolean(r.OptimalCostMatch)),
        ("cost_deviation",          true,  r => Real(r.CostDeviation, "E3")),
    };

    private readonly StreamWriter _writer;

    /// <summary>The file being appended to.</summary>
    public string Path { get; }

    /// <summary>Rows this recorder has written.</summary>
    public int RowsWritten { get; private set; }

    /// <summary>Whether the file already existed and is being appended to.</summary>
    public bool Appending { get; }

    public static string Header => string.Join(FieldSeparator, Columns.Select(column => column.Name));

    /// <summary>
    /// Opens the file for appending, creating it and its directory if needed. An
    /// existing file must carry exactly the expected header: appending rows with
    /// a different shape would corrupt the study's only data file, so a mismatch
    /// is an error rather than something to work around.
    /// </summary>
    public CsvRecorder(string path)
    {
        Path = path;

        string? directory = System.IO.Path.GetDirectoryName(path);
        if (!string.IsNullOrEmpty(directory))
            Directory.CreateDirectory(directory);

        Appending = File.Exists(path) && new FileInfo(path).Length > 0;
        if (Appending)
        {
            string? existingHeader = File.ReadLines(path).FirstOrDefault();
            if (existingHeader != Header)
                throw new InvalidDataException(
                    $"{path} already exists with a different set of columns, so rows cannot be " +
                    $"appended to it. Move it aside, or write to another file.\n" +
                    $"  found:    {existingHeader}\n" +
                    $"  expected: {Header}");
        }

        // Flushed per row: a long invocation that is interrupted must leave every
        // completed run on disk.
        _writer = new StreamWriter(path, append: true) { AutoFlush = true };

        if (!Appending)
            _writer.WriteLine(Header);
    }

    public void Write(RunRecord record)
    {
        var fields = new string[Columns.Length];
        for (int i = 0; i < Columns.Length; i++)
        {
            string value = Columns[i].Value(record);

            // No field in this schema can legitimately contain a separator or a
            // quote, so rather than implement quoting, assert the assumption the
            // reader will make. The Excel separator is checked too, because the
            // viewing copy re-joins these same fields with it.
            if (value.Contains(FieldSeparator) || value.Contains(ExcelFieldSeparator)
                || value.Contains('"') || value.Contains('\n'))
                throw new InvalidDataException(
                    $"Column '{Columns[i].Name}' contains a character that would need quoting: '{value}'.");

            fields[i] = value;
        }

        _writer.WriteLine(string.Join(FieldSeparator, fields));
        RowsWritten++;
    }

    public void Dispose() => _writer.Dispose();

    // ----------------------------------------------------------- Excel view

    /// <summary>
    /// Writes a semicolon-delimited, comma-decimal copy of the canonical file,
    /// for opening in a spreadsheet on a locale that uses a comma for decimals
    /// — where double-clicking <c>runs.csv</c> puts every field in one column.
    /// <para>
    /// <b>Purely for viewing, and purely derived.</b> The canonical file stays
    /// the data: it is what the reviewer receives and what the statistics tools
    /// read. This copy is rewritten from it in full every time, so it always
    /// covers every configuration accumulated so far, it can be regenerated at
    /// any time without re-running an experiment, and it cannot drift. Nothing
    /// should ever be read back out of it.
    /// </para>
    /// <para>
    /// Only columns declared <c>Numeric</c> have their decimal separator
    /// rewritten, which is what keeps the <c>0x…</c> seeds text rather than
    /// letting a spreadsheet mangle them into scientific notation.
    /// </para>
    /// </summary>
    public static void WriteExcelCopy(string canonicalPath, string excelPath)
    {
        if (!File.Exists(canonicalPath))
            throw new FileNotFoundException(
                $"There is no {canonicalPath} to build a viewing copy from — run a configuration first.",
                canonicalPath);

        string? directory = System.IO.Path.GetDirectoryName(excelPath);
        if (!string.IsNullOrEmpty(directory))
            Directory.CreateDirectory(directory);

        using var reader = new StreamReader(canonicalPath);
        using var writer = new StreamWriter(excelPath, append: false);

        int lineNumber = 0;
        string? line;
        while ((line = reader.ReadLine()) is not null)
        {
            lineNumber++;
            if (line.Length == 0)
                continue;

            string[] fields = line.Split(FieldSeparator);
            if (fields.Length != Columns.Length)
                throw new InvalidDataException(
                    $"{canonicalPath} line {lineNumber} has {fields.Length} fields, expected {Columns.Length}. " +
                    $"The file looks damaged — a spreadsheet may have saved over it.");

            // Line 1 is the header, whose names contain no decimal point.
            if (lineNumber > 1)
                for (int i = 0; i < fields.Length; i++)
                    if (Columns[i].Numeric)
                        fields[i] = fields[i].Replace(DecimalSeparator, ExcelDecimalSeparator);

            writer.WriteLine(string.Join(ExcelFieldSeparator, fields));
        }
    }

    // ------------------------------------------------------------ formatting

    private static string Seed(ulong seed) => "0x" + seed.ToString("X16", CultureInfo.InvariantCulture);

    private static string Integer(long value) => value.ToString(CultureInfo.InvariantCulture);

    private static string Integer(long? value) =>
        value.HasValue ? Integer(value.Value) : "";

    // InvariantCulture on every numeric field: this machine's locale uses a
    // comma as the decimal separator, which would both break parsing and collide
    // with the field separator.
    private static string Real(double? value, string format) =>
        value.HasValue ? value.Value.ToString(format, CultureInfo.InvariantCulture) : "";

    private static string Boolean(bool? value) =>
        value.HasValue ? (value.Value ? "TRUE" : "FALSE") : "";
}
