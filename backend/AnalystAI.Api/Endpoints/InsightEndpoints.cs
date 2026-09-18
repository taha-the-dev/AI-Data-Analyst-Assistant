using System.Diagnostics;
using AnalystAI.Api.Analysis;
using AnalystAI.Api.Contracts;
using AnalystAI.Api.Data;
using AnalystAI.Api.Services;
using Microsoft.EntityFrameworkCore;

namespace AnalystAI.Api.Endpoints;

/// <summary>
/// Dashboard and Analytics. Both read the uploaded file's own columns: the
/// dashboard chooses what to show, Analytics lets the user choose.
/// </summary>
public static class InsightEndpoints
{
    /// <summary>Roles the dashboard assigns, in the order they make a good first metric.</summary>
    private static readonly string[] MetricRoles =
        ["Score", "Revenue", "Salary", "Quantity", "Profit", "Measure", "Subject marks", "Attendance", "Performance", "Tenure", "Age", "Unit price"];

    private static readonly string[] DimensionRoles =
        ["Subject", "Category", "Department", "Class", "Region", "Grade", "Product", "Grouping", "Role", "Status", "Gender", "Date", "Hire date"];

    public static RouteGroupBuilder MapInsightEndpoints(this RouteGroupBuilder api)
    {
        api.MapGet("/dashboard", async (
            AppDbContext db, DashboardService dashboards, IDatasetContext context,
            int? datasetId, CancellationToken ct) =>
        {
            var id = await context.ResolveAsync(datasetId, ct);
            if (id is null) return Problems.NoDataset(datasetId);

            var dataset = await db.Datasets.AsNoTracking().FirstAsync(d => d.Id == id, ct);
            var dashboard = await dashboards.GetAsync(dataset, ct);

            return dashboard is null ? Problems.NoSource(dataset.Name) : Results.Ok(dashboard);
        })
        .WithTags("Insights")
        .WithName("GetDashboard")
        .WithSummary("A dashboard written for the file: tiles, charts, table, insights and data quality chosen from its own columns.");

        var analytics = api.MapGroup("/analytics").WithTags("Insights");

        analytics.MapGet("/fields", async (
            AppDbContext db, SourceStore sources, DashboardService dashboards, IDatasetContext context,
            int? datasetId, CancellationToken ct) =>
        {
            var (loaded, problem) = await FrameAccess.LoadAsync(db, sources, context, datasetId, ct);
            if (problem is not null) return problem;
            var frame = loaded!.Frame;

            var metrics = frame.Columns.Where(IsMetric).ToList();
            var dimensions = frame.Columns.Where(c => IsDimension(c, frame.RowCount)).ToList();

            // Open on what the dashboard recognised, so the first view is the
            // one that matters for this kind of data.
            var roles = (await dashboards.GetAsync(loaded.Dataset, ct))?.Fields ?? [];
            string? Pick(IEnumerable<Column> candidates, string[] preferred) =>
                preferred
                    .SelectMany(role => roles.Where(f => f.Role == role))
                    .Select(f => candidates.FirstOrDefault(c => c.Name == f.Column))
                    .FirstOrDefault(c => c is not null) is { } hit
                    ? FrameQuery.Key(hit)
                    : candidates.Select(FrameQuery.Key).FirstOrDefault();

            var metricFields = metrics.Select(c => new AnalyticsFieldDto(FrameQuery.Key(c), c.Name, c.Kind, FrameAccess.Unit(c))).ToList();
            var defaultMetric = Pick(metrics, MetricRoles);

            // A file with quantity and price but no revenue column: offer the
            // revenue the dashboard derived from them, and open on it.
            var quantity = metrics.FirstOrDefault(c => roles.Any(f => f.Role == "Quantity" && f.Column == c.Name));
            var price = metrics.FirstOrDefault(c => roles.Any(f => f.Role == "Unit price" && f.Column == c.Name));
            if (quantity is not null && price is not null && !roles.Any(f => f.Role == "Revenue"))
            {
                var derived = Derived(frame, $"{FrameQuery.Key(quantity)}*{FrameQuery.Key(price)}")!;
                defaultMetric = $"{FrameQuery.Key(quantity)}*{FrameQuery.Key(price)}";
                metricFields.Insert(0, new AnalyticsFieldDto(defaultMetric, derived.Name, "number", FrameAccess.Unit(derived)));
            }

            var defaultDimension = Pick(dimensions.Where(d => !d.IsDate || dimensions.All(x => x.IsDate)), DimensionRoles);
            var metricColumn = Derived(frame, defaultMetric) ?? FrameQuery.Find(frame, defaultMetric);

            return Results.Ok(new AnalyticsFieldsDto(
                metricFields,
                dimensions.Select(c => new AnalyticsFieldDto(FrameQuery.Key(c), c.Name, c.IsDate ? "date" : c.IsNumber ? "number" : FrameAccess.Role(c), FrameAccess.Unit(c))).ToList(),
                frame.Columns.Where(c => c.IsGroup(50) || (c.IsNumber && !c.Identifier && c.Distinct <= Math.Min(12, c.Present / 2))).Select(FrameAccess.Describe).ToList(),
                defaultMetric,
                defaultDimension,
                metricColumn is null ? "count" : DatasetAnalyzer.Additive(metricColumn.Name) ? "sum" : "avg",
                frame.RowCount));
        })
        .WithName("AnalyticsFields")
        .WithSummary("The metrics, dimensions and filters this file supports, with sensible defaults.");

        analytics.MapGet("/run", async (
            AppDbContext db, SourceStore sources, IDatasetContext context, HttpRequest request,
            int? datasetId, string? metric, string? dimension, string? bucket,
            string aggregate = "avg", int limit = 50, CancellationToken ct = default) =>
        {
            var (loaded, problem) = await FrameAccess.LoadAsync(db, sources, context, datasetId, ct);
            if (problem is not null) return problem;
            var frame = loaded!.Frame;
            var watch = Stopwatch.StartNew();

            aggregate = aggregate.ToLowerInvariant();
            if (!FrameQuery.Aggregates.Contains(aggregate))
                return Problems.BadRequest("Unknown aggregate", $"'{aggregate}' is not supported. Use one of {string.Join(", ", FrameQuery.Aggregates)}.");

            if (FrameQuery.Find(frame, dimension) is not { } dim)
                return FrameAccess.UnknownColumn(frame, "dimension", dimension ?? "");

            Column? measure = null;
            if (aggregate != "count")
            {
                measure = Derived(frame, metric) ?? FrameQuery.Find(frame, metric);
                if (measure is null) return FrameAccess.UnknownColumn(frame, "metric", metric ?? "");
                if (!measure.IsNumber)
                    return Problems.BadRequest("That column holds no numbers",
                        $"'{measure.Name}' is not numeric, so it can only be counted. Use aggregate=count or pick a numeric metric.");
            }

            var (filters, filterProblem) = FrameAccess.ParseFilters(frame, request.Query["filter"]);
            if (filterProblem is not null) return filterProblem;

            var rows = FrameQuery.Filter(frame, filters);
            var grain = dim.IsDate ? FrameQuery.BucketFor(dim, rows, bucket) : null;
            var grouped = FrameQuery.Group(frame, rows, dim, grain, measure, aggregate, Math.Clamp(limit, 1, 200));

            var dimensionLabel = grain is null ? Fmt.Lower(dim.Name) : grain;
            var title = measure is null
                ? $"Rows by {dimensionLabel}"
                : $"{AggregateWord(aggregate)} {Fmt.Lower(measure.Name)} by {dimensionLabel}";

            var unit = measure is null ? Unit.Count : aggregate is "avg" or "median" ? Unit.For(measure).ForAverage : Unit.For(measure);
            var summary = measure is null ? null : FrameQuery.Summarise(measure, rows);

            return Results.Ok(new AnalyticsResultDto(
                title,
                measure?.Name,
                dim.Name,
                aggregate,
                grain,
                new ColumnUnitDto(unit.Prefix, unit.Suffix, unit.Decimals),
                grouped.Figures,
                aggregate is "sum" or "count" ? grouped.Total : null,
                grouped.GroupCount,
                rows.Count,
                frame.RowCount,
                (int)watch.ElapsedMilliseconds,
                summary is null ? null : new MetricSummaryDto(summary.Count, summary.Sum, summary.Mean, summary.Min, summary.Max, summary.Median)));
        })
        .WithName("AnalyticsRun")
        .WithSummary("Aggregate one of the file's columns by another, with repeatable filter=column:op:value.");

        return api;
    }

    private static bool IsMetric(Column c) => c.IsNumber && !c.Identifier && c.Present > 0;

    /// <summary>
    /// Anything rows can sensibly be grouped by: dates, repeated labels, and
    /// numbers with only a few values (a rating, a year, a class level).
    /// </summary>
    private static bool IsDimension(Column c, int rows) =>
        c.Present > 0 && !c.Identifier && (
            c.IsDate
            || (c.IsNumber && c.Distinct <= Math.Min(24, c.Present / 2))
            || (!c.IsNumber && c.Distinct >= 1 && c.Distinct <= 1000 && c.Distinct < Math.Max(2, rows)));

    /// <summary>
    /// "c5*c6": the product of two numeric columns, row by row — revenue from
    /// quantity and unit price. Built per request; the shared frame is never changed.
    /// </summary>
    private static Column? Derived(Frame frame, string? key)
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

        var name = a.Match(["qty", "quantity", "units"]) > 0 && b.Match(["price", "rate"]) > 0 ? "Revenue" : $"{a.Name} × {b.Name}";
        return new Column
        {
            Index = -1, Name = name, Tokens = Frame.Tokenise(name), Compact = name.ToLowerInvariant(),
            Kind = "number", Values = a.Values, Present = present, Distinct = 0,
            Numbers = values, Prefix = a.Prefix.Length > 0 ? a.Prefix : b.Prefix,
        };
    }

    private static string AggregateWord(string aggregate) => aggregate switch
    {
        "sum" => "Total",
        "avg" => "Average",
        "min" => "Lowest",
        "max" => "Highest",
        "median" => "Median",
        _ => "Count of",
    };
}
