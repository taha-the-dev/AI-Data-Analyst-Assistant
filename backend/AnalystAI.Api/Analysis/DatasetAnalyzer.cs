using AnalystAI.Api.Contracts;
using AnalystAI.Api.Models;
using AnalystAI.Api.Query;
using AnalystAI.Api.Services;

namespace AnalystAI.Api.Analysis;

/// <summary>
/// Writes a dashboard for one uploaded file.
///
/// The dashboard used to be the same four money tiles for every file, so a
/// class register came back as "Revenue $0". Now the file is read column by
/// column, the headers and value types decide what kind of data it is, and a
/// recipe for that kind picks the tiles, charts, table and insights it can
/// actually compute. Anything a recipe cannot fill is topped up from what the
/// file does contain, and anything that cannot be computed is left out.
/// </summary>
public static class DatasetAnalyzer
{
    private static readonly (string Domain, string Label, string[] Words)[] Domains =
    [
        ("education", "Student performance",
            ["student", "students", "pupil", "learner", "marks", "mark", "subject", "course", "attendance", "exam",
             "grade", "gpa", "cgpa", "semester", "roll", "teacher", "school", "class", "quiz", "assignment", "score"]),
        ("sales", "Sales & orders",
            ["revenue", "sales", "sale", "order", "orders", "product", "price", "qty", "quantity", "customer", "invoice",
             "discount", "profit", "sku", "units", "store", "shipping", "amount", "item"]),
        ("hr", "Workforce & HR",
            ["employee", "emp", "salary", "department", "dept", "designation", "position", "hire", "hired", "joining",
             "tenure", "manager", "attrition", "bonus", "overtime", "experience", "payroll", "staff", "leave"]),
    ];

    public static DashboardDto Analyze(CsvProfiler.ParseResult parsed, IReadOnlyList<DatasetColumn> profile) =>
        Analyze(Frame.From(parsed, profile));

    internal static DashboardDto Analyze(Frame frame)
    {
        var board = new Board(frame);
        var (domain, label) = Detect(frame);

        switch (domain)
        {
            case "education": Education.Build(board); break;
            case "sales": Sales.Build(board); break;
            case "hr": Hr.Build(board); break;
        }

        // A recipe only fills what its columns allow. Whatever is still empty
        // is filled from the file's own measures and groupings.
        General.Build(board, primary: domain == "general");

        var quality = Quality(frame);
        return new DashboardDto(
            domain,
            label,
            Summary(frame, label, domain, board),
            board.Fields,
            board.Kpis,
            board.Charts,
            board.Table,
            board.Insights(QualityInsight(frame, quality)),
            quality);
    }

    /// <summary>
    /// Scores each kind of data by how many headers speak its vocabulary. Two
    /// clear matches are needed, so one "score" column does not make a file
    /// about students.
    /// </summary>
    private static (string Domain, string Label) Detect(Frame frame)
    {
        var scored = Domains
            .Select(d => (d.Domain, d.Label, Score: frame.Columns.Sum(c => c.Match(d.Words) switch { 3 => 2, 1 => 1, _ => 0 })))
            .OrderByDescending(d => d.Score)
            .ToList();

        var best = scored[0];
        if (best.Score < 4 || best.Score == scored[1].Score) return ("general", "General dataset");
        return (best.Domain, best.Label);
    }

    private static string Summary(Frame frame, string label, string domain, Board board)
    {
        var fields = board.Fields.Count;
        var read = $"Read {Fmt.Int(frame.RowCount)} rows across {frame.Columns.Count} columns";
        return domain == "general"
            ? $"{read}. No specific subject area was recognised, so the dashboard is built from the file's own measures and groupings."
            : $"{read} and recognised {Noun(domain)} data from {fields} of its fields.";
    }

    private static string Noun(string domain) => domain switch
    {
        "education" => "student performance",
        "sales" => "sales and order",
        "hr" => "HR and workforce",
        _ => domain,
    };

    // ── Data quality ────────────────────────────────────────────────────────

    private static DataQualityDto Quality(Frame frame)
    {
        var cells = Math.Max(1L, (long)frame.RowCount * frame.Columns.Count);
        var missing = frame.Columns.Sum(c => (long)c.Missing);
        var completeness = frame.RowCount == 0 ? 0 : 1d - missing / (double)cells;
        var uniqueness = frame.RowCount == 0 ? 0 : 1d - frame.DuplicateRows / (double)frame.RowCount;

        // Share of the values in each typed column that actually read as that type.
        var consistency = frame.Columns.Count == 0
            ? 1d
            : frame.Columns.Average(c => c.Present == 0
                ? 1d
                : c.IsNumber ? c.Numbers.Count(n => n.HasValue) / (double)c.Present
                : c.IsDate ? c.Dates.Count(d => d.HasValue) / (double)c.Present
                : 1d);

        // Floored, so a file with any gap or repeat never reads as a perfect 100.
        var score = (int)Math.Floor(100 * (0.6 * completeness + 0.25 * uniqueness + 0.15 * consistency) + 1e-9);
        var grade = score >= 90 ? "High" : score >= 75 ? "Medium" : "Low";

        var issues = frame.Columns
            .Where(c => c.Missing > 0)
            .OrderByDescending(c => c.Missing)
            .Take(6)
            .Select(c => new ColumnIssueDto(c.Name, c.Missing, Math.Round(c.Present / (double)Math.Max(1, frame.RowCount), 4)))
            .ToList();

        return new DataQualityDto(frame.RowCount, frame.Columns.Count, missing, frame.DuplicateRows,
            Math.Round(completeness, 4), score, grade, issues);
    }

    private static InsightDto QualityInsight(Frame frame, DataQualityDto quality)
    {
        if (frame.RowCount == 0)
            return new InsightDto("alert", "warning_amber", "No data rows", "The file has a header row but no rows beneath it.");

        if (quality.MissingCells == 0 && quality.DuplicateRows == 0)
            return new InsightDto("positive", "check_circle", "Data quality",
                $"All {Fmt.Int(frame.RowCount * (long)frame.Columns.Count)} cells are filled and no row is repeated.");

        var parts = new List<string>();
        if (quality.MissingCells > 0)
        {
            var worst = quality.Issues[0];
            parts.Add($"{Fmt.Int(quality.MissingCells)} cells are empty, most of them in {worst.Column} ({Fmt.Int(worst.Missing)})");
        }
        if (quality.DuplicateRows > 0)
            parts.Add($"{Fmt.Int(quality.DuplicateRows)} {(quality.DuplicateRows == 1 ? "row is an exact duplicate" : "rows are exact duplicates")}");

        return new InsightDto("alert", "warning_amber", "Data quality",
            $"{string.Join("; ", parts)}.{(quality.MissingCells > 0 ? " Averages above leave the gaps out." : "")}");
    }

    // ── Shared by the recipes ───────────────────────────────────────────────

    /// <summary>The last period against the one before it, or null if that cannot be said.</summary>
    internal static (string Text, string Tone, double Percent)? Change(IReadOnlyList<Figure> series)
    {
        if (series.Count < 2) return null;
        var last = series[^1].Value;
        var previous = series[^2].Value;
        if (Math.Abs(previous) < 1e-12) return null;

        var percent = (last - previous) / Math.Abs(previous) * 100;
        return ($"{(percent >= 0 ? "+" : "")}{percent:0.0}%", percent >= 0 ? "up" : "down", percent);
    }

    internal static DataTableDto Table(string title, string? caption, IEnumerable<(string Label, string Align)> columns, IEnumerable<string[]> rows) =>
        new(title, caption, columns.Select(c => new TableColumnDto(c.Label, c.Align)).ToList(), rows.ToList());

    /// <summary>Headers that name something you add up rather than average.</summary>
    internal static bool Additive(string name) =>
        Frame.Tokenise(name).Any(t => t is "amount" or "total" or "count" or "qty" or "quantity" or "sales" or "revenue"
            or "cost" or "spend" or "units" or "volume" or "hours" or "expense" or "expenses" or "sum" or "value" or "profit"
            or "income" or "budget" or "visits" or "clicks" or "views" or "downloads");

    /// <summary>The share a figure is of a total, written as a percentage.</summary>
    internal static string Share(double part, double total) => total <= 0 ? "—" : Fmt.Pct(part / total);
}
