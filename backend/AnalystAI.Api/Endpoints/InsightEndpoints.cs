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

            var metrics = frame.Columns.Where(c => c.CanMeasure).ToList();
            var dimensions = frame.Columns.Where(c => c.CanGroupBy).ToList();

            // Open on what the dashboard recognised, so the first view is the
            // one that matters for this kind of data. The assistant reads the
            // same schema, so both screens agree on what the file is about.
            var schema = DatasetSchema.From(frame, await dashboards.GetAsync(loaded.Dataset, ct));
            string? KeyOf(string? name)
            {
                if (schema.Find(name) is not { } column) return null;
                if (column.Expression is not null) return column.Expression;
                return FrameQuery.Find(frame, column.Name) is { } c ? FrameQuery.Key(c) : null;
            }

            var metricFields = metrics.Select(c => new AnalyticsFieldDto(FrameQuery.Key(c), c.Name, c.Kind, FrameAccess.Unit(c))).ToList();
            foreach (var derived in schema.Measures.Where(m => m.Expression is not null))
                metricFields.Insert(0, new AnalyticsFieldDto(derived.Expression!, derived.Name, "number",
                    FrameAccess.Unit(FrameEngine.Derived(frame, derived.Expression, derived.Name)!)));

            var defaultMetric = KeyOf(schema.DefaultMetric);
            var defaultDimension = KeyOf(schema.DefaultDimension)
                                   ?? dimensions.Select(FrameQuery.Key).FirstOrDefault();
            var metricColumn = schema.Find(schema.DefaultMetric);

            return Results.Ok(new AnalyticsFieldsDto(
                metricFields,
                dimensions.Select(c => new AnalyticsFieldDto(FrameQuery.Key(c), c.Name, c.IsDate ? "date" : c.IsNumber ? "number" : c.Role, FrameAccess.Unit(c))).ToList(),
                frame.Columns.Where(c => c.CanFilterByValue).Select(FrameAccess.Describe).ToList(),
                defaultMetric,
                defaultDimension,
                metricColumn is null ? "count" : metricColumn.Additive ? "sum" : "avg",
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
                measure = FrameEngine.Derived(frame, metric) ?? FrameQuery.Find(frame, metric);
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
                new ValueUnitDto(unit.Prefix, unit.Suffix, unit.Decimals),
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
