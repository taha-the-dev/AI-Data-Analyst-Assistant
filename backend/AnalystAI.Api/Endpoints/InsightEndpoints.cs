using AnalystAI.Api.Contracts;
using AnalystAI.Api.Data;
using AnalystAI.Api.Query;
using AnalystAI.Api.Services;
using Microsoft.EntityFrameworkCore;

namespace AnalystAI.Api.Endpoints;

/// <summary>
/// Dashboard and Analytics. The dashboard is written from the uploaded file's
/// own columns; Analytics runs saved <see cref="QuerySpec"/>s through the engine.
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

            return dashboard is null
                ? Results.Problem(
                    title: "Upload this file again to build its dashboard",
                    detail: $"'{dataset.Name}' was uploaded before files were kept for analysis, so only its sales-shaped rows survive. " +
                            "Upload the original file again and the dashboard will be built from every column it has.",
                    statusCode: StatusCodes.Status409Conflict)
                : Results.Ok(dashboard);
        })
        .WithTags("Insights")
        .WithName("GetDashboard")
        .WithSummary("A dashboard written for the file: tiles, charts, table, insights and data quality chosen from its own columns.");

        api.MapGet("/analytics", async (
            AppDbContext db, QueryEngine engine, IDatasetContext context,
            int? datasetId, CancellationToken ct) =>
        {
            var id = await context.ResolveAsync(datasetId, ct);
            if (id is null) return Problems.NoDataset(datasetId);

            var byCategory = await engine.RunAsync(SavedSpecs.RevenueByCategory(), id.Value, ct);
            var byRegion = await engine.RunAsync(SavedSpecs.RevenueByRegion(), id.Value, ct);
            var trend = await engine.RunAsync(SavedSpecs.RevenueByMonth(), id.Value, ct);
            var ordersByMonth = await engine.RunAsync(SavedSpecs.OrdersByMonth(), id.Value, ct);
            var customers = await engine.RunAsync(SavedSpecs.TopCustomers(), id.Value, ct);

            var orderCount = await db.SalesRows.CountAsync(r => r.DatasetId == id, ct);
            var inProcessing = await db.SalesRows
                .CountAsync(r => r.DatasetId == id && r.Status == "Processing", ct);

            return Results.Ok(new AnalyticsDto(
                KpiBuilder.ForRevenue(byCategory.Total, orderCount, inProcessing, trend.Figures, ordersByMonth.Figures),
                byCategory.Figures,
                byRegion.Figures,
                customers.Figures,
                trend.Figures));
        })
        .WithTags("Insights")
        .WithName("GetAnalytics")
        .WithSummary("Aggregate figures for the file in context, with period-over-period deltas.");

        return api;
    }
}
