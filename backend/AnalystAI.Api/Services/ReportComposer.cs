using System.Globalization;
using AnalystAI.Api.Contracts;
using AnalystAI.Api.Models;
using AnalystAI.Api.Query;

namespace AnalystAI.Api.Services;

/// <summary>
/// Writes a report body from the file's own analysis, at read time, so a report
/// can never drift from the data it claims to describe.
///
/// It used to run three fixed sales queries — revenue by category, by month,
/// top products — and wrote about dollars whatever the file held. It now reads
/// the same analysis the dashboard shows: the tiles become the summary, each
/// chart becomes a figure with a sentence computed from its values, and the
/// insights close it. A mark sheet gets a report about marks.
/// </summary>
internal sealed class ReportComposer(DashboardService dashboards)
{
    private static readonly CultureInfo Inv = CultureInfo.InvariantCulture;

    /// <param name="source">
    /// The dataset the figures are actually computed from. Passed in rather than
    /// read off the report, because a report stores the name it was written for
    /// and that name is not proof the file is still there.
    /// </param>
    /// <returns>Null when no copy of the file was kept to compose from.</returns>
    public async Task<ReportDetailDto?> ComposeAsync(
        Report report, Dataset source, CancellationToken ct = default)
    {
        var dashboard = await dashboards.GetAsync(source, ct);
        if (dashboard is null) return null;

        var sections = new List<ReportSectionDto>();

        var tiles = dashboard.Kpis
            .Select(k => $"{k.Label} {k.Value}{(k.Hint is null ? "" : $" ({k.Hint})")}")
            .ToList();
        sections.Add(new ReportSectionDto("Summary",
        [
            dashboard.Summary,
            tiles.Count == 0 ? "The file holds no measure to summarise." : string.Join(" · ", tiles) + ".",
        ], null));

        var number = 0;
        foreach (var chart in dashboard.Charts)
        {
            number++;
            sections.Add(new ReportSectionDto(chart.Title,
                [Describe(chart)],
                new ReportFigureDto(number, chart.Caption is null ? chart.Title : $"{chart.Title} — {chart.Caption}",
                    chart.Kind, chart.Figures, chart.Unit, dashboard.Quality.Rows)));
        }

        // The quality insight is left for the section of its own below.
        var findings = dashboard.Insights.Where(i => i.Title != "Data quality").ToList();
        if (findings.Count > 0)
            sections.Add(new ReportSectionDto("Findings", findings.Select(i => $"{i.Title}. {i.Body}").ToArray(), null));

        var q = dashboard.Quality;
        sections.Add(new ReportSectionDto("Data quality",
        [
            $"{q.Rows:N0} rows across {q.Columns} columns, {q.Completeness * 100:0.#}% complete, with "
            + $"{q.MissingCells:N0} empty cells and {q.DuplicateRows:N0} duplicate rows. Quality score {q.Score} of 100 ({q.Grade.ToLowerInvariant()}).",
        ], null));

        var meta = $"Generated from {source.Name} · {dashboard.DomainLabel} · {q.Rows:N0} rows · {number} figures";

        return new ReportDetailDto(
            new ReportDto(report.Id, report.Title, report.DatasetName, number, report.Status, report.CreatedAt),
            meta, sections);
    }

    /// <summary>One sentence stating what a chart shows, from its own values.</summary>
    private static string Describe(ChartDto chart)
    {
        var f = chart.Figures;
        if (f.Count == 0) return "Nothing to plot.";
        string V(double v) => chart.Unit.Prefix + v.ToString("N" + chart.Unit.Decimals, Inv) + chart.Unit.Suffix;

        switch (chart.Kind)
        {
            case "line":
                var peak = f.MaxBy(x => x.Value)!;
                return $"Across {f.Count} periods the figure moves from {V(f[0].Value)} in {Analysis.Fmt.Period(f[0].Label)} "
                       + $"to {V(f[^1].Value)} in {Analysis.Fmt.Period(f[^1].Label)}, peaking at {V(peak.Value)} in {Analysis.Fmt.Period(peak.Label)}.";

            case "columns":
                var band = f.MaxBy(x => x.Value)!;
                var total = f.Sum(x => x.Value);
                return $"The largest band is {band.Label}, holding {band.Value:N0} of {total:N0} ({(total == 0 ? 0 : band.Value / total * 100):0.#}%).";

            case "donut":
                var sum = f.Sum(x => x.Value);
                return "The split is " + string.Join(", ", f.Select(x => $"{x.Label} {(sum == 0 ? 0 : x.Value / sum * 100):0.#}%")) + ".";

            default:
                var ranked = f.OrderByDescending(x => x.Value).ToList();
                return ranked.Count == 1
                    ? $"{ranked[0].Label} stands at {V(ranked[0].Value)}."
                    : $"{ranked[0].Label} is highest at {V(ranked[0].Value)} and {ranked[^1].Label} lowest at {V(ranked[^1].Value)}, across {ranked.Count} groups.";
        }
    }
}
