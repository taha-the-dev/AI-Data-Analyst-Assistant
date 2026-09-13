using System.Diagnostics;
using System.Globalization;
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

        IQueryable<SalesRow> q = db.SalesRows.Where(r => r.DatasetId == datasetId);
        foreach (var f in spec.Filters)
            q = RowFilters.Apply(q, f);

        // Filtering happens in SQL; grouping is done in memory because the
        // group-by column is chosen at runtime. Fine at this size — revisit
        // with raw SQL if a dataset ever runs to millions of rows.
        var rows = await q.ToListAsync(ct);
        var matched = rows.Count;

        var groups = rows
            .GroupBy(r => GroupKey(r, spec))
            .Select(g => new Figure(g.Key, Aggregate(g, spec)))
            .ToList();

        groups = (spec.Sort switch
        {
            "value asc" => groups.OrderBy(f => f.Value),
            "label asc" => groups.OrderBy(f => f.Label, StringComparer.Ordinal),
            "label desc" => groups.OrderByDescending(f => f.Label, StringComparer.Ordinal),
            _ => groups.OrderByDescending(f => f.Value),
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

    private static string GroupKey(SalesRow r, QuerySpec spec)
    {
        if (spec.TimeBucket is not null)
        {
            var d = r.Date;
            // A row whose date could not be read is reported as such rather
            // than landing in whatever bucket the default date falls in.
            if (d == RowFilters.UndatedValue) return RowFilters.UndatedLabel;

            return spec.TimeBucket switch
            {
                "day" => d.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture),
                "week" => $"{ISOWeek.GetYear(d.ToDateTime(TimeOnly.MinValue))}-W{ISOWeek.GetWeekOfYear(d.ToDateTime(TimeOnly.MinValue)):00}",
                "quarter" => $"{d.Year}-Q{(d.Month - 1) / 3 + 1}",
                "year" => d.Year.ToString(CultureInfo.InvariantCulture),
                _ => d.ToString("yyyy-MM", CultureInfo.InvariantCulture),
            };
        }

        return (spec.GroupBy?.ToLowerInvariant()) switch
        {
            "customer" => r.Customer,
            "product" => r.Product,
            "region" => r.Region,
            "status" => r.Status,
            "orderid" => r.OrderId,
            "date" => r.Date == RowFilters.UndatedValue
                ? RowFilters.UndatedLabel
                : r.Date.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture),
            _ => r.Category,
        };
    }

    private static double Value(SalesRow r, string? metric) => metric?.ToLowerInvariant() switch
    {
        "qty" => r.Qty,
        "price" => r.Price,
        _ => r.Revenue,
    };

    private static double Aggregate(IEnumerable<SalesRow> group, QuerySpec spec) => spec.Aggregate switch
    {
        "count" => group.Count(),
        "avg" => Math.Round(group.Average(r => Value(r, spec.Metric)), 2),
        "min" => group.Min(r => Value(r, spec.Metric)),
        "max" => group.Max(r => Value(r, spec.Metric)),
        _ => Math.Round(group.Sum(r => Value(r, spec.Metric)), 2),
    };

}
