using System.Diagnostics;
using System.Globalization;
using System.Linq.Expressions;
using AnalystAI.Api.Data;
using AnalystAI.Api.Models;
using Microsoft.EntityFrameworkCore;

namespace AnalystAI.Api.Query;

/// <summary>
/// Executes a <see cref="QuerySpec"/> against the stored rows. This is the only
/// place a figure is ever produced.
/// </summary>
public class QueryEngine(AppDbContext db)
{
    public static readonly string[] Columns =
        ["date", "orderId", "customer", "product", "category", "qty", "price", "revenue", "region", "status"];

    public static readonly string[] Aggregates = ["sum", "avg", "count", "min", "max"];

    public static bool IsColumn(string name) =>
        Columns.Contains(name, StringComparer.OrdinalIgnoreCase);

    public static bool IsAggregate(string name) =>
        Aggregates.Contains(name, StringComparer.OrdinalIgnoreCase);

    public async Task<QueryResult> RunAsync(QuerySpec spec, int datasetId, CancellationToken ct = default)
    {
        var sw = Stopwatch.StartNew();

        var scanned = await db.SalesRows.CountAsync(r => r.DatasetId == datasetId, ct);

        IQueryable<SalesRow> q = db.SalesRows.AsNoTracking().Where(r => r.DatasetId == datasetId);
        foreach (var f in spec.Filters)
            q = RowFilters.Apply(q, f);

        // The database groups and aggregates, so a request costs memory in
        // proportion to its groups rather than its rows: loading every row of a
        // large file into memory is what ran the hosted instance out of it.
        //
        // What comes back is one line per stored key — a text value, or a
        // date — carrying its count, sum, minimum and maximum. Labels that SQL
        // cannot produce the same way on SQLite and PostgreSQL (ISO weeks,
        // quarters, "Undated") are made here by merging those lines, and every
        // aggregate can be rebuilt exactly from the four parts.
        var buckets = UsesDate(spec)
            ? (await GroupAsync(q, r => r.Date, spec.Metric, ct))
                .Select(b => (Label: DateLabel(b.Key, spec.TimeBucket), b.Count, b.Sum, b.Min, b.Max))
            : (await GroupAsync(q, TextKey(spec.GroupBy), spec.Metric, ct))
                .Select(b => (Label: b.Key, b.Count, b.Sum, b.Min, b.Max));

        var merged = buckets
            .GroupBy(b => b.Label, StringComparer.Ordinal)
            .Select(g => (
                Label: g.Key,
                Count: g.Sum(b => b.Count),
                Sum: g.Sum(b => b.Sum),
                Min: g.Min(b => b.Min),
                Max: g.Max(b => b.Max)))
            .ToList();

        var matched = merged.Sum(g => (long)g.Count);

        var groups = merged
            .Select(g => new Figure(g.Label, Aggregate(spec.Aggregate, g.Count, g.Sum, g.Min, g.Max)))
            .ToList();

        // Equal values are ordered by label. The database returns groups in
        // whatever order it grouped them, which differs between SQLite and
        // PostgreSQL, so without this a tie — every customer's largest order
        // being 20 units, say — would rank differently from one host to the next.
        groups = (spec.Sort switch
        {
            "value asc" => groups.OrderBy(f => f.Value).ThenBy(f => f.Label, StringComparer.Ordinal),
            "label asc" => groups.OrderBy(f => f.Label, StringComparer.Ordinal),
            "label desc" => groups.OrderByDescending(f => f.Label, StringComparer.Ordinal),
            _ => groups.OrderByDescending(f => f.Value).ThenBy(f => f.Label, StringComparer.Ordinal),
        }).ToList();

        // A trend must read left to right in time regardless of magnitude.
        if (spec.Intent == "trend")
            groups = groups.OrderBy(f => f.Label, StringComparer.Ordinal).ToList();

        if (spec.Limit > 0)
            groups = groups.Take(spec.Limit).ToList();

        sw.Stop();

        return new QueryResult
        {
            Spec = spec,
            Figures = groups,
            RowsScanned = scanned,
            RowsMatched = matched,
            DurationMs = (int)sw.ElapsedMilliseconds,
            Total = groups.Sum(f => f.Value),
            Unit = spec.Aggregate == "count" || spec.Metric == "qty" ? "count" : "currency",
        };
    }

    private static bool UsesDate(QuerySpec spec) =>
        spec.TimeBucket is not null || string.Equals(spec.GroupBy, "date", StringComparison.OrdinalIgnoreCase);

    private static Expression<Func<SalesRow, string>> TextKey(string? groupBy) =>
        groupBy?.ToLowerInvariant() switch
        {
            "customer" => r => r.Customer,
            "product" => r => r.Product,
            "region" => r => r.Region,
            "status" => r => r.Status,
            "orderid" => r => r.OrderId,
            _ => r => r.Category,
        };

    private static string DateLabel(DateOnly d, string? bucket)
    {
        // A row whose date could not be read is reported as such rather than
        // landing in whatever bucket the default date falls in.
        if (d == RowFilters.UndatedValue) return RowFilters.UndatedLabel;

        return bucket switch
        {
            null or "day" => d.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture),
            "week" => $"{ISOWeek.GetYear(d.ToDateTime(TimeOnly.MinValue))}-W{ISOWeek.GetWeekOfYear(d.ToDateTime(TimeOnly.MinValue)):00}",
            "quarter" => $"{d.Year}-Q{(d.Month - 1) / 3 + 1}",
            "year" => d.Year.ToString(CultureInfo.InvariantCulture),
            _ => d.ToString("yyyy-MM", CultureInfo.InvariantCulture),
        };
    }

    private static double Aggregate(string aggregate, int count, double sum, double min, double max) => aggregate switch
    {
        "count" => count,
        "avg" => Math.Round(sum / count, 2),
        "min" => min,
        "max" => max,
        _ => Math.Round(sum, 2),
    };

    /// <summary>One key's count, sum, minimum and maximum, computed in SQL.</summary>
    private static Task<List<Bucket<TKey>>> GroupAsync<TKey>(
        IQueryable<SalesRow> q, Expression<Func<SalesRow, TKey>> key, string? metric, CancellationToken ct) =>
        q.Select(Point(key, metric))
            .GroupBy(p => p.Key)
            .Select(g => new Bucket<TKey>
            {
                Key = g.Key,
                Count = g.Count(),
                Sum = g.Sum(p => p.Value),
                Min = g.Min(p => p.Value),
                Max = g.Max(p => p.Value),
            })
            .ToListAsync(ct);

    /// <summary>
    /// Pairs the grouping key with the metric being measured. Both are chosen at
    /// runtime, so the projection is built as an expression — which keeps it
    /// translatable to SQL — rather than as one lambda per combination.
    /// </summary>
    private static Expression<Func<SalesRow, KeyedValue<TKey>>> Point<TKey>(
        Expression<Func<SalesRow, TKey>> key, string? metric)
    {
        var row = key.Parameters[0];
        Expression value = metric?.ToLowerInvariant() switch
        {
            "qty" => Expression.Convert(Expression.Property(row, nameof(SalesRow.Qty)), typeof(double)),
            "price" => Expression.Property(row, nameof(SalesRow.Price)),
            _ => Expression.Property(row, nameof(SalesRow.Revenue)),
        };

        var type = typeof(KeyedValue<TKey>);
        return Expression.Lambda<Func<SalesRow, KeyedValue<TKey>>>(
            Expression.MemberInit(
                Expression.New(type),
                Expression.Bind(type.GetProperty(nameof(KeyedValue<TKey>.Key))!, key.Body),
                Expression.Bind(type.GetProperty(nameof(KeyedValue<TKey>.Value))!, value)),
            row);
    }

    private sealed class KeyedValue<TKey>
    {
        public TKey Key { get; init; } = default!;
        public double Value { get; init; }
    }

    private sealed class Bucket<TKey>
    {
        public TKey Key { get; init; } = default!;
        public int Count { get; init; }
        public double Sum { get; init; }
        public double Min { get; init; }
        public double Max { get; init; }
    }
}
