using AnalystAI.Api.Contracts;
using AnalystAI.Api.Models;
using AnalystAI.Api.Query;

namespace AnalystAI.Api.Services;

/// <summary>
/// Writes a report body from figures computed at read time, so a report can
/// never drift from the data it claims to describe. The prose is assembled from
/// the numbers; the numbers are never assembled to fit the prose.
/// </summary>
public class ReportComposer(QueryEngine engine)
{
    /// <param name="sourceName">
    /// The name of the dataset the figures were actually computed from. Passed
    /// in rather than read off the report, because a report stores the name it
    /// was written for and that name is not proof the file is still there.
    /// </param>
    public async Task<ReportDetailDto> ComposeAsync(
        Report report, int datasetId, string sourceName, CancellationToken ct = default)
    {
        var byCategory = await engine.RunAsync(SavedSpecs.RevenueByCategory(), datasetId, ct);
        var trend = await engine.RunAsync(SavedSpecs.RevenueByMonth(), datasetId, ct);
        var topProducts = await engine.RunAsync(SavedSpecs.TopProducts(8), datasetId, ct);

        var top = byCategory.Figures.FirstOrDefault();
        var peak = trend.Figures.Count > 0 ? trend.Figures.MaxBy(f => f.Value) : null;
        var topThree = byCategory.Figures.Take(3).Sum(f => f.Value);

        var sections = new List<ReportSectionDto>
        {
            new("Summary",
            [
                $"This file holds {byCategory.RowsScanned:N0} rows totalling ${byCategory.Total:N0} in revenue across {byCategory.Figures.Count} categories.",
                top is null
                    ? "No category carries revenue yet."
                    : $"{top.Label} is the largest at ${top.Value:N0}, and the top three together account for {topThree / Math.Max(1, byCategory.Total) * 100:0.0}% of the total.",
            ], null),

            new("Where the revenue came from",
            [
                "Every figure below was computed against the stored rows. The spec that produced each one travels with the figure.",
            ], new ReportFigureDto(1, "Revenue by category", "ranked",
                byCategory.Figures, byCategory.Spec, byCategory.RowsScanned)),

            new("The shape of the period",
            [
                peak is null
                    ? "There are not enough dated rows to plot a trend."
                    : $"Monthly revenue peaks at ${peak.Value:N0} in {peak.Label}, across {trend.Figures.Count} months of data.",
            ], new ReportFigureDto(2, "Revenue by month", "trend",
                trend.Figures, trend.Spec, trend.RowsScanned)),

            new("What sold",
            [
                topProducts.Figures.Count == 0
                    ? "No product-level rows are stored for this file."
                    : $"{topProducts.Figures[0].Label} leads on revenue at ${topProducts.Figures[0].Value:N0}, out of {topProducts.Figures.Count} products ranked here.",
            ], new ReportFigureDto(3, "Top products by revenue", "ranked",
                topProducts.Figures, topProducts.Spec, topProducts.RowsScanned)),
        };

        var meta = $"Generated from {sourceName} · {byCategory.RowsScanned:N0} rows · "
                 + $"{sections.Count(s => s.Figure is not null)} figures";

        return new ReportDetailDto(
            new ReportDto(report.Id, report.Title, report.DatasetName, report.Figures, report.Status, report.CreatedAt),
            meta, sections);
    }
}
