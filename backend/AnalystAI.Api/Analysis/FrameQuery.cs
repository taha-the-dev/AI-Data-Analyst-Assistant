using System.Globalization;
using AnalystAI.Api.Query;
using AnalystAI.Api.Services;

namespace AnalystAI.Api.Analysis;

/// <summary>A rule on one column: <c>marks gt 50</c>, <c>subject eq Physics</c>.</summary>
internal sealed record CellFilter(Column Column, string Op, string Value);

/// <summary>
/// Filtering, sorting and grouping over the file's own columns — what the Data
/// Explorer and Analytics run on. Columns are addressed as <c>c0</c>, <c>c1</c>…
/// by position, since headers can repeat or be blank.
/// </summary>
internal static class FrameQuery
{
    public static readonly string[] Ops = ["eq", "ne", "gt", "gte", "lt", "lte", "contains"];
    public static readonly string[] Aggregates = ["sum", "avg", "count", "min", "max", "median"];
    public static readonly string[] Buckets = ["day", "month", "quarter", "year"];

    public static string Key(Column column) => $"c{column.Index}";

    /// <summary>A column by key ("c3"), or by header name, ignoring case.</summary>
    public static Column? Find(Frame frame, string? key)
    {
        if (string.IsNullOrWhiteSpace(key)) return null;
        if (key.Length > 1 && key[0] == 'c' && int.TryParse(key[1..], NumberStyles.None, CultureInfo.InvariantCulture, out var i))
            return i < frame.Columns.Count ? frame.Columns[i] : null;
        return frame.Columns.FirstOrDefault(c => c.Name.Equals(key.Trim(), StringComparison.OrdinalIgnoreCase));
    }

    /// <summary>Row numbers that pass every rule, in file order.</summary>
    public static List<int> Filter(Frame frame, IReadOnlyList<CellFilter> filters)
    {
        var rows = new List<int>(frame.RowCount);
        var parsed = filters.Select(f => (
            Filter: f,
            Number: Cells.TryNumber(f.Value, out var n) ? n : (double?)null,
            Date: Cells.TryDate(f.Value, out var d) ? d : (DateOnly?)null)).ToList();

        for (var r = 0; r < frame.RowCount; r++)
            if (parsed.All(p => Matches(p.Filter, r, p.Number, p.Date))) rows.Add(r);

        return rows;
    }

    private static bool Matches(CellFilter filter, int row, double? number, DateOnly? date)
    {
        var column = filter.Column;
        var text = column.Values[row];
        if (text.Length == 0) return filter.Op == "ne";

        if (filter.Op == "contains") return text.Contains(filter.Value.Trim(), StringComparison.OrdinalIgnoreCase);

        int? compared = null;
        if (column.IsNumber && number is { } n && column.Numbers[row] is { } v) compared = v.CompareTo(n);
        else if (column.IsDate && date is { } d && column.Dates[row] is { } cell) compared = cell.CompareTo(d);

        if (compared is null)
        {
            // Text, or a value that did not read as the column's type.
            if (filter.Op is "eq" or "ne")
            {
                var equal = text.Equals(filter.Value.Trim(), StringComparison.OrdinalIgnoreCase);
                return filter.Op == "eq" ? equal : !equal;
            }
            if (column.IsNumber || column.IsDate) return false;
            compared = string.Compare(text, filter.Value.Trim(), StringComparison.OrdinalIgnoreCase);
        }

        return filter.Op switch
        {
            "eq" => compared == 0,
            "ne" => compared != 0,
            "gt" => compared > 0,
            "gte" => compared >= 0,
            "lt" => compared < 0,
            "lte" => compared <= 0,
            _ => false,
        };
    }

    /// <summary>Sorted by a column's own type; empty cells always go last.</summary>
    public static void Sort(List<int> rows, Column column, bool descending)
    {
        var sign = descending ? -1 : 1;
        Comparison<int> compare = column switch
        {
            { IsNumber: true } => (a, b) => Order(column.Numbers[a], column.Numbers[b], sign),
            { IsDate: true } => (a, b) => Order(column.Dates[a], column.Dates[b], sign),
            _ => (a, b) =>
            {
                var x = column.Values[a];
                var y = column.Values[b];
                if (x.Length == 0 || y.Length == 0) return x.Length == 0 ? (y.Length == 0 ? a.CompareTo(b) : 1) : -1;
                var c = string.Compare(x, y, StringComparison.OrdinalIgnoreCase) * sign;
                return c != 0 ? c : a.CompareTo(b);
            },
        };

        rows.Sort(compare);

        static int Order<T>(T? x, T? y, int sign) where T : struct, IComparable<T>
        {
            if (x is null || y is null) return x is null ? (y is null ? 0 : 1) : -1;
            return x.Value.CompareTo(y.Value) * sign;
        }
    }

    /// <summary>The grain for a date dimension: the one asked for, or whichever suits the span.</summary>
    public static string BucketFor(Column date, IReadOnlyList<int> rows, string? requested)
    {
        if (requested is not null && Buckets.Contains(requested)) return requested;

        DateOnly? min = null, max = null;
        foreach (var r in rows)
        {
            if (date.Dates[r] is not { } d) continue;
            if (min is null || d < min) min = d;
            if (max is null || d > max) max = d;
        }
        if (min is null || max is null) return "month";

        var span = max.Value.DayNumber - min.Value.DayNumber;
        return span <= 45 ? "day" : span <= 365 * 3 ? "month" : "year";
    }

    private static string DateKey(DateOnly d, string bucket) => bucket switch
    {
        "day" => d.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture),
        "quarter" => $"{d.Year}-Q{(d.Month - 1) / 3 + 1}",
        "year" => d.Year.ToString(CultureInfo.InvariantCulture),
        _ => d.ToString("yyyy-MM", CultureInfo.InvariantCulture),
    };

    public sealed record Grouped(List<Figure> Figures, int GroupCount, double Total);

    /// <summary>
    /// One figure per value of <paramref name="dimension"/> (or per period, for a
    /// date). Dates and numbers keep their natural order; anything else is ranked
    /// by value, largest first.
    /// </summary>
    public static Grouped Group(Frame frame, IReadOnlyList<int> rows, Column dimension, string? bucket,
        Column? metric, string aggregate, int limit)
    {
        var buckets = new Dictionary<string, Bucket>(StringComparer.OrdinalIgnoreCase);

        foreach (var r in rows)
        {
            string key;
            if (dimension.IsDate)
            {
                if (dimension.Dates[r] is not { } d) continue;
                key = DateKey(d, bucket ?? "month");
            }
            else
            {
                key = dimension.Values[r].Length == 0 ? "Unspecified" : dimension.Values[r];
            }

            if (!buckets.TryGetValue(key, out var b)) buckets[key] = b = new Bucket();
            b.Rows++;

            if (aggregate == "count" || metric?.Numbers[r] is not { } v) continue;
            b.Add(v, aggregate == "median");
        }

        var figures = buckets
            .Where(kv => aggregate == "count" || kv.Value.Count > 0)
            .Select(kv => new Figure(kv.Key, Frame.Round(kv.Value.Value(aggregate))))
            .ToList();

        var total = aggregate is "sum" or "count" ? figures.Sum(f => f.Value) : 0;

        IEnumerable<Figure> ordered = dimension.IsDate
            ? figures.OrderBy(f => f.Label, StringComparer.Ordinal)
            : dimension.IsNumber
                ? figures.OrderBy(f => Cells.TryNumber(f.Label, out var n) ? n : double.MaxValue)
                : figures.OrderByDescending(f => f.Value).ThenBy(f => f.Label, StringComparer.OrdinalIgnoreCase);

        // A trend keeps its most recent periods; a ranking keeps its leaders.
        var shown = dimension.IsDate ? ordered.TakeLast(limit) : ordered.Take(limit);
        return new Grouped(shown.ToList(), figures.Count, total);
    }

    public sealed record Summary(int Count, double Sum, double Mean, double Min, double Max, double Median);

    public static Summary? Summarise(Column metric, IReadOnlyList<int> rows)
    {
        var values = rows.Where(r => metric.Numbers[r].HasValue).Select(r => metric.Numbers[r]!.Value).Order().ToList();
        if (values.Count == 0) return null;
        return new Summary(values.Count, Frame.Round(values.Sum()), Frame.Round(values.Average()),
            values[0], values[^1], Frame.Round(Frame.Percentile(values, 0.5)));
    }

    private sealed class Bucket
    {
        public int Rows;
        public int Count;
        private double _sum;
        private double _min = double.MaxValue;
        private double _max = double.MinValue;
        private List<double>? _values;

        public void Add(double v, bool keep)
        {
            Count++;
            _sum += v;
            if (v < _min) _min = v;
            if (v > _max) _max = v;
            if (keep) (_values ??= []).Add(v);
        }

        public double Value(string aggregate) => aggregate switch
        {
            "count" => Rows,
            "sum" => _sum,
            "avg" => Count == 0 ? 0 : _sum / Count,
            "min" => Count == 0 ? 0 : _min,
            "max" => Count == 0 ? 0 : _max,
            "median" => _values is null ? 0 : Frame.Percentile([.. _values.Order()], 0.5),
            _ => 0,
        };
    }
}
