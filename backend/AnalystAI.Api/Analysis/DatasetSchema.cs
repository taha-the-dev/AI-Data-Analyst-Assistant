using System.Globalization;
using System.Text;
using AnalystAI.Api.Contracts;

namespace AnalystAI.Api.Analysis;

/// <summary>
/// One column as a planner sees it. Kind is number | date | category | text |
/// identifier. <see cref="Expression"/> marks a derived measure such as revenue
/// from quantity × price ("c5*c6"); it has no column of its own in the file.
/// </summary>
public sealed record SchemaColumn(
    string Name,
    string Kind,
    IReadOnlyList<string> Values,
    int Distinct,
    string? Range,
    string? Role,
    bool CanMeasure,
    bool CanGroupBy,
    bool Additive,
    string? Expression = null);

/// <summary>
/// What a planner is told about the file in context: every column, what each
/// holds, and which measure and grouping the dashboard recognised as the point
/// of the file.
///
/// Planners used to be handed a paragraph of text and pull values back out of it
/// by string-scanning. This is the same knowledge, typed: the keyword planner
/// reads it directly and <see cref="Describe"/> writes it out for a model.
/// </summary>
public sealed record DatasetSchema(
    string Domain,
    string DomainLabel,
    long Rows,
    IReadOnlyList<SchemaColumn> Columns,
    string? DefaultMetric,
    string? DefaultDimension,
    string? DateColumn)
{
    /// <summary>A column by name, ignoring case.</summary>
    public SchemaColumn? Find(string? name) =>
        string.IsNullOrWhiteSpace(name)
            ? null
            : Columns.FirstOrDefault(c => c.Name.Equals(name.Trim(), StringComparison.OrdinalIgnoreCase));

    public IEnumerable<SchemaColumn> Measures => Columns.Where(c => c.CanMeasure);

    public IEnumerable<SchemaColumn> Groupings => Columns.Where(c => c.CanGroupBy);

    /// <summary>The schema as a model reads it: one line per column.</summary>
    public string Describe()
    {
        var width = Math.Min(24, Columns.Select(c => c.Name.Length).DefaultIfEmpty(4).Max()) + 2;
        var text = new StringBuilder();
        foreach (var c in Columns)
        {
            text.Append("  ").Append(c.Name.PadRight(width)).Append(c.Kind.PadRight(11));

            var detail = c.Expression is not null
                ? "derived: " + c.Expression
                : c.Kind == "category" && c.Values.Count > 0
                    ? string.Join(", ", c.Values)
                    : c.Range ?? (c.Kind is "text" or "identifier" ? $"{c.Distinct:N0} distinct values" : "");
            text.Append(detail);
            if (c.Role is not null) text.Append("   (").Append(c.Role).Append(')');
            text.AppendLine();
        }
        return text.ToString().TrimEnd();
    }

    /// <summary>The largest number of values a category lists before it is described by its size instead.</summary>
    private const int ValueCeiling = 25;

    /// <summary>
    /// Built from the file itself, with the roles the dashboard assigned, so the
    /// assistant and the dashboard agree on what the file is about.
    /// </summary>
    internal static DatasetSchema From(Frame frame, DashboardDto? dashboard)
    {
        var roles = dashboard?.Fields ?? [];
        string? RoleOf(Column c) => roles.FirstOrDefault(f => f.Column == c.Name)?.Role;

        var columns = frame.Columns.Select(c =>
        {
            var kind = c.Identifier ? "identifier" : c.IsNumber ? "number" : c.IsDate ? "date" : c.IsGroup(ValueCeiling) ? "category" : "text";
            var values = kind == "category" ? c.FilterOptions() ?? [] : [];

            string? range = null;
            if (c.IsNumber)
            {
                var present = c.Numbers.Where(n => n.HasValue).Select(n => n!.Value).ToList();
                if (present.Count > 0)
                    range = $"from {Fmt.Value(present.Min(), Unit.For(c))} to {Fmt.Value(present.Max(), Unit.For(c))}";
            }
            else if (c.IsDate)
            {
                var present = c.Dates.Where(d => d.HasValue).Select(d => d!.Value).ToList();
                if (present.Count > 0)
                    range = $"from {present.Min():yyyy-MM-dd} to {present.Max():yyyy-MM-dd}";
            }

            return new SchemaColumn(c.Name, kind, values, c.Distinct, range, RoleOf(c),
                c.CanMeasure, c.CanGroupBy, DatasetAnalyzer.Additive(c.Name));
        }).ToList();

        // Revenue the file never states, but the dashboard derived from quantity
        // and unit price: the assistant can answer in it too.
        var quantity = frame.Columns.FirstOrDefault(c => c.CanMeasure && RoleOf(c) == "Quantity");
        var price = frame.Columns.FirstOrDefault(c => c.CanMeasure && RoleOf(c) == "Unit price");
        string? derived = null;
        if (quantity is not null && price is not null && roles.All(f => f.Role != "Revenue"))
        {
            derived = "Revenue";
            columns.Insert(0, new SchemaColumn(derived, "number", [], 0, $"{quantity.Name} × {price.Name}", "Revenue",
                CanMeasure: true, CanGroupBy: false, Additive: true,
                Expression: $"{FrameQuery.Key(quantity)}*{FrameQuery.Key(price)}"));
        }

        string? Prefer(IEnumerable<SchemaColumn> candidates, string[] preferred)
        {
            var list = candidates.ToList();
            return preferred.Select(role => list.FirstOrDefault(c => c.Role == role)).FirstOrDefault(c => c is not null)?.Name
                   ?? list.FirstOrDefault()?.Name;
        }

        var measures = columns.Where(c => c.CanMeasure).ToList();
        var groupings = columns.Where(c => c.CanGroupBy && c.Kind != "date").ToList();

        return new DatasetSchema(
            dashboard?.Domain ?? "general",
            dashboard?.DomainLabel ?? "General dataset",
            frame.RowCount,
            columns,
            derived ?? Prefer(measures, MetricRoles),
            Prefer(groupings, DimensionRoles),
            frame.Date?.Name);
    }

    /// <summary>Roles the dashboard assigns, in the order they make a good first measure.</summary>
    internal static readonly string[] MetricRoles =
        ["Score", "Revenue", "Salary", "Quantity", "Profit", "Measure", "Subject marks", "Attendance", "Performance", "Tenure", "Age", "Unit price"];

    /// <summary>…and a good first grouping.</summary>
    internal static readonly string[] DimensionRoles =
        ["Subject", "Category", "Department", "Class", "Region", "Grade", "Product", "Grouping", "Role", "Status", "Gender"];
}

/// <summary>
/// Runs a <see cref="Query.QuerySpec"/> over a file's frame. Every figure the
/// assistant states comes from here.
/// </summary>
internal static class FrameEngine
{
    public static Query.QueryResult Run(Frame frame, DatasetSchema schema, Query.QuerySpec spec)
    {
        var watch = System.Diagnostics.Stopwatch.StartNew();

        var dimension = FrameQuery.Find(frame, spec.GroupBy)
                        ?? throw new ArgumentException($"'{spec.GroupBy}' is not a column. Normalise the spec first.");
        var metric = spec.Aggregate == "count" ? null : Measure(frame, schema, spec.Metric);

        var filters = spec.Filters
            .Select(f => (Column: FrameQuery.Find(frame, f.Column), Op: FrameQuery.OpCode(f.Op), f.Value))
            .Where(f => f.Column is not null && f.Op is not null)
            .Select(f => new CellFilter(f.Column!, f.Op!, f.Value))
            .ToList();

        var rows = FrameQuery.Filter(frame, filters);
        var bucket = dimension.IsDate ? FrameQuery.BucketFor(dimension, rows, spec.TimeBucket) : null;
        var grouped = FrameQuery.Group(frame, rows, dimension, bucket, metric, metric is null ? "count" : spec.Aggregate, 200);

        IEnumerable<Query.Figure> figures = spec.Sort switch
        {
            "value asc" => grouped.Figures.OrderBy(f => f.Value),
            "value desc" when !dimension.IsDate => grouped.Figures.OrderByDescending(f => f.Value),
            "label desc" => grouped.Figures.OrderByDescending(f => f.Label, StringComparer.OrdinalIgnoreCase),
            _ => grouped.Figures,
        };
        var limited = dimension.IsDate ? figures.TakeLast(spec.Limit).ToList() : figures.Take(spec.Limit).ToList();

        var unit = metric is null ? Unit.Count : spec.Aggregate is "avg" or "median" ? Unit.For(metric).ForAverage : Unit.For(metric);

        return new Query.QueryResult
        {
            Spec = spec with { TimeBucket = bucket, Metric = metric?.Name },
            Figures = limited,
            RowsScanned = frame.RowCount,
            RowsMatched = rows.Count,
            DurationMs = (int)watch.ElapsedMilliseconds,
            Total = spec.Aggregate is "sum" or "count" ? limited.Sum(f => f.Value) : 0,
            Unit = unit.ToDto(),
            MetricLabel = metric?.Name,
        };
    }

    /// <summary>A measure by name: a numeric column, or a derived one the schema describes.</summary>
    public static Column? Measure(Frame frame, DatasetSchema schema, string? name)
    {
        var described = schema.Find(name);
        if (described?.Expression is { } expression) return Derived(frame, expression, described.Name);
        return FrameQuery.Find(frame, name) is { IsNumber: true } column ? column : null;
    }

    /// <summary>
    /// "c5*c6": the product of two numeric columns, row by row — revenue from
    /// quantity and unit price. Built per request; the shared frame is never changed.
    /// </summary>
    public static Column? Derived(Frame frame, string? key, string? name = null)
    {
        if (key?.Split('*') is not [var left, var right]) return null;
        if (FrameQuery.Find(frame, left) is not { IsNumber: true } a || FrameQuery.Find(frame, right) is not { IsNumber: true } b) return null;

        var values = new double?[frame.RowCount];
        var present = 0;
        for (var r = 0; r < frame.RowCount; r++)
        {
            if (a.Numbers[r] is not { } x || b.Numbers[r] is not { } y) continue;
            values[r] = x * y;
            present++;
        }

        name ??= a.Match(["qty", "quantity", "units"]) > 0 && b.Match(["price", "rate"]) > 0 ? "Revenue" : $"{a.Name} × {b.Name}";
        return new Column
        {
            Index = -1, Name = name, Tokens = Frame.Tokenise(name), Compact = name.ToLowerInvariant(),
            Kind = "number", Values = a.Values, Present = present, Distinct = 0,
            Numbers = values, Prefix = a.Prefix.Length > 0 ? a.Prefix : b.Prefix,
        };
    }
}
