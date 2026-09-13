using AnalystAI.Api.Contracts;
using AnalystAI.Api.Data;
using AnalystAI.Api.Query;
using AnalystAI.Api.Services;
using Microsoft.EntityFrameworkCore;

namespace AnalystAI.Api.Endpoints;

/// <summary>
/// Dashboard and Analytics. Both are saved <see cref="QuerySpec"/>s run through
/// the engine — no figure on either screen is hard-coded.
/// </summary>
public static class InsightEndpoints
{
    public static RouteGroupBuilder MapInsightEndpoints(this RouteGroupBuilder api)
    {
        api.MapGet("/dashboard", async (
            AppDbContext db, QueryEngine engine, IDatasetContext context,
            int? datasetId, CancellationToken ct) =>
        {
            var id = await context.ResolveAsync(datasetId, ct);
            if (id is null) return Problems.NoDataset(datasetId);

            var dataset = await db.Datasets.AsNoTracking()
                .Include(d => d.Columns)
                .FirstAsync(d => d.Id == id, ct);

            var trend = await engine.RunAsync(SavedSpecs.RevenueByMonth(), id.Value, ct);
            var orders = await engine.RunAsync(SavedSpecs.OrdersByMonth(), id.Value, ct);
            var byRegion = await engine.RunAsync(SavedSpecs.RevenueByRegion(), id.Value, ct);
            var byCategory = await engine.RunAsync(SavedSpecs.RevenueByCategory(), id.Value, ct);

            var missing = dataset.Columns.Sum(c => (long)c.Missing);
            var orderCount = await db.SalesRows.CountAsync(r => r.DatasetId == id, ct);

            return Results.Ok(new DashboardDto(
                KpiBuilder.ForDataset(dataset, missing),
                trend.Figures,
                byRegion.Figures,
                byCategory.Figures,
                KpiBuilder.Insights(dataset, missing, byCategory, trend, orderCount)));
        })
        .WithTags("Insights")
        .WithName("GetDashboard")
        .WithSummary("KPI strip, trend, regional split, category bars and generated insights.");

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
