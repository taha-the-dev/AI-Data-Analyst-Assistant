using AnalystAI.Api.Contracts;
using AnalystAI.Api.Data;
using AnalystAI.Api.Models;
using AnalystAI.Api.Security;
using AnalystAI.Api.Services;
using Microsoft.EntityFrameworkCore;

namespace AnalystAI.Api.Endpoints;

public static class ReportEndpoints
{
    /// <summary>Shown in place of "Ready" once a report's source file is deleted.</summary>
    private const string SourceDeleted = "Source deleted";

    public static RouteGroupBuilder MapReportEndpoints(this RouteGroupBuilder api)
    {
        var group = api.MapGroup("/reports").WithTags("Reports");

        group.MapGet("/", async (AppDbContext db, CancellationToken ct) =>
        {
            // A report body is composed from live figures, so a report whose
            // source file has been deleted has nothing to compose from. The
            // status says so in the list rather than letting someone open a row
            // that cannot be read.
            var items = await db.Reports.AsNoTracking()
                .OrderByDescending(r => r.CreatedAt)
                .Select(r => new ReportDto(
                    r.Id, r.Title, r.DatasetName, r.Figures,
                    db.Datasets.Any(d => d.Name == r.DatasetName) ? r.Status : SourceDeleted,
                    r.CreatedAt))
                .ToListAsync(ct);

            return Results.Ok(items);
        })
        .WithName("ListReports")
        .WithSummary("Every generated report, most recent first.");

        // A report is a title and a source file. Its body is composed from live
        // figures every time it is read, so nothing is stored that could drift
        // from the data it describes.
        group.MapPost("/", async (
            AppDbContext db, IDatasetContext context, ReportComposer composer,
            CreateReportRequest? body, CancellationToken ct) =>
        {
            var datasetId = await context.ResolveAsync(body?.DatasetId, ct);
            if (datasetId is null) return Problems.NoDataset(body?.DatasetId);

            var dataset = await db.Datasets.AsNoTracking().FirstAsync(d => d.Id == datasetId, ct);
            if (!await db.DatasetSources.AnyAsync(s => s.DatasetId == dataset.Id, ct))
                return Problems.NoSource(dataset.Name);

            var report = new Report
            {
                Title = InputLimits.Clip(
                    string.IsNullOrWhiteSpace(body?.Title) ? $"{dataset.Name} review" : body.Title.Trim(),
                    InputLimits.TitleLength),
                DatasetName = dataset.Name,
                Status = "Ready",
                CreatedAt = DateTime.UtcNow,
            };

            db.Reports.Add(report);
            await db.SaveChangesAsync(ct);

            // Composed once here so Figures is the number the reader will
            // actually show, rather than a guess stored beside it.
            var composed = await composer.ComposeAsync(report, dataset, ct);
            report.Figures = composed?.Sections.Count(s => s.Figure is not null) ?? 0;
            await db.SaveChangesAsync(ct);

            return Results.Created($"/api/reports/{report.Id}",
                new ReportDto(report.Id, report.Title, report.DatasetName, report.Figures, report.Status, report.CreatedAt));
        })
        .WithName("CreateReport")
        .WithSummary("Write a report for a dataset; its body is composed from live figures on read.");

        group.MapGet("/{id:int}", async (
            AppDbContext db, ReportComposer composer,
            int id, int? datasetId, CancellationToken ct) =>
        {
            var report = await db.Reports.AsNoTracking().FirstOrDefaultAsync(r => r.Id == id, ct);
            if (report is null) return Problems.NotFound("Report", id);

            // A report names the file it was written for, and the body is
            // composed from that file's rows at read time. Falling back to
            // whatever else is loaded used to keep the page rendering, but it
            // put one file's figures under another file's name with nothing on
            // screen to say so. A deleted source is now reported as gone.
            var source = await db.Datasets.AsNoTracking()
                .Where(d => d.Id == datasetId || (datasetId == null && d.Name == report.DatasetName))
                .OrderByDescending(d => d.UpdatedAt)
                .FirstOrDefaultAsync(ct);

            if (source is null)
                return datasetId is > 0
                    ? Problems.NoDataset(datasetId)
                    : Results.Problem(
                        title: "The file this report was written for is gone",
                        detail: $"'{report.DatasetName}' has been deleted, and a report's figures are "
                              + "computed from its rows every time it is read. Upload the file again to "
                              + "restore the report, or delete the report. To read it against a different "
                              + "file deliberately, pass ?datasetId=.",
                        statusCode: StatusCodes.Status410Gone);

            var composed = await composer.ComposeAsync(report, source, ct);
            return composed is null ? Problems.NoSource(source.Name) : Results.Ok(composed);
        })
        .WithName("GetReport")
        .WithSummary("One report, composed at read time from the file's own analysis.");

        group.MapDelete("/{id:int}", async (AppDbContext db, int id, CancellationToken ct) =>
        {
            var report = await db.Reports.FirstOrDefaultAsync(r => r.Id == id, ct);
            if (report is null) return Problems.NotFound("Report", id);

            db.Reports.Remove(report);
            await db.SaveChangesAsync(ct);

            return Results.NoContent();
        })
        .WithName("DeleteReport")
        .WithSummary("Delete a report.");

        return api;
    }
}
