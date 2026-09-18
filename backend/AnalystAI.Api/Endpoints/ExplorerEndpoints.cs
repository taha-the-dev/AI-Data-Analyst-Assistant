using System.Globalization;
using AnalystAI.Api.Analysis;
using AnalystAI.Api.Contracts;
using AnalystAI.Api.Data;
using AnalystAI.Api.Services;

namespace AnalystAI.Api.Endpoints;

/// <summary>
/// The Data Explorer grid. It shows the file as it was uploaded — its own
/// headers, every column, values as written — rather than the sales-shaped rows
/// the query engine stores, which had no place for a marks or salary column.
/// </summary>
public static class ExplorerEndpoints
{
    public static RouteGroupBuilder MapExplorerEndpoints(this RouteGroupBuilder api)
    {
        var group = api.MapGroup("/explorer").WithTags("Explorer");

        group.MapGet("/columns", async (
            AppDbContext db, SourceStore sources, IDatasetContext context, int? datasetId, CancellationToken ct) =>
        {
            var (loaded, problem) = await FrameAccess.LoadAsync(db, sources, context, datasetId, ct);
            if (problem is not null) return problem;
            var frame = loaded!.Frame;

            CoverageDto? coverage = null;
            if (frame.Date is { } date)
            {
                var dates = date.Dates.Where(d => d.HasValue).Select(d => d!.Value).ToList();
                if (dates.Count > 0)
                    coverage = new CoverageDto(date.Name,
                        dates.Min().ToString("yyyy-MM-dd", CultureInfo.InvariantCulture),
                        dates.Max().ToString("yyyy-MM-dd", CultureInfo.InvariantCulture));
            }

            return Results.Ok(new ExplorerSchemaDto(frame.Columns.Select(FrameAccess.Describe).ToList(), frame.RowCount, coverage));
        })
        .WithName("ExplorerColumns")
        .WithSummary("The file's own columns: header, type, role, unit and, for groupings, their values.");

        group.MapGet("/rows", async (
            AppDbContext db,
            SourceStore sources,
            HttpRequest request,
            IDatasetContext context,
            int? datasetId,
            int page = 1,
            int pageSize = 25,
            string? sort = null,
            string dir = "asc",
            CancellationToken ct = default) =>
        {
            page = Math.Max(1, page);
            pageSize = Math.Clamp(pageSize, 1, 500);

            var (loaded, problem) = await FrameAccess.LoadAsync(db, sources, context, datasetId, ct);
            if (problem is not null) return problem;
            var frame = loaded!.Frame;

            Column? sortColumn = null;
            if (!string.IsNullOrWhiteSpace(sort) && (sortColumn = FrameQuery.Find(frame, sort)) is null)
                return FrameAccess.UnknownColumn(frame, "sort column", sort);

            var (filters, filterProblem) = FrameAccess.ParseFilters(frame, request.Query["filter"]);
            if (filterProblem is not null) return filterProblem;

            var rows = FrameQuery.Filter(frame, filters);
            if (sortColumn is not null)
                FrameQuery.Sort(rows, sortColumn, dir.Equals("desc", StringComparison.OrdinalIgnoreCase));

            var items = rows.Skip((page - 1) * pageSize).Take(pageSize)
                .Select(r => new ExplorerRowDto(r + 1, frame.Columns.Select(c => c.Values[r]).ToList()))
                .ToList();

            return Results.Ok(new Paged<ExplorerRowDto>(
                items, page, pageSize, rows.Count, (int)Math.Ceiling(rows.Count / (double)pageSize)));
        })
        .WithName("ExplorerRows")
        .WithSummary("Paged rows of the file, sorted by any column and filtered with repeatable filter=column:op:value.");

        return api;
    }
}
