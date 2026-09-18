using AnalystAI.Api.Contracts;
using AnalystAI.Api.Data;
using AnalystAI.Api.Models;
using AnalystAI.Api.Security;
using AnalystAI.Api.Services;
using Microsoft.EntityFrameworkCore;

namespace AnalystAI.Api.Endpoints;

public static class DatasetEndpoints
{
    public static RouteGroupBuilder MapDatasetEndpoints(this RouteGroupBuilder api)
    {
        var group = api.MapGroup("/datasets").WithTags("Datasets");

        group.MapGet("/", async (
            AppDbContext db,
            string? search,
            int page = 1,
            int pageSize = 6,
            string sort = "name",
            string dir = "asc",
            CancellationToken ct = default) =>
        {
            page = Math.Max(1, page);
            pageSize = Math.Clamp(pageSize, 1, 100);

            var q = db.Datasets.AsNoTracking();

            if (!string.IsNullOrWhiteSpace(search))
            {
                var term = search.Trim();
                // Lowered on both sides: LIKE ignores case in SQLite but not in
                // PostgreSQL, and a search should behave the same on either.
                var pattern = $"%{term.ToLowerInvariant()}%";
                q = q.Where(d => EF.Functions.Like(d.Name.ToLower(), pattern));
            }

            var descending = dir.Equals("desc", StringComparison.OrdinalIgnoreCase);
            q = sort.ToLowerInvariant() switch
            {
                "rows" => descending ? q.OrderByDescending(d => d.RowCount) : q.OrderBy(d => d.RowCount),
                "columns" => descending ? q.OrderByDescending(d => d.ColumnCount) : q.OrderBy(d => d.ColumnCount),
                "type" => descending ? q.OrderByDescending(d => d.Type) : q.OrderBy(d => d.Type),
                "quality" => descending ? q.OrderByDescending(d => d.Quality) : q.OrderBy(d => d.Quality),
                "updated" => descending ? q.OrderByDescending(d => d.UpdatedAt) : q.OrderBy(d => d.UpdatedAt),
                _ => descending ? q.OrderByDescending(d => d.Name) : q.OrderBy(d => d.Name),
            };

            var total = await q.CountAsync(ct);
            var items = await q.Skip((page - 1) * pageSize).Take(pageSize)
                .Select(d => Map(d)).ToListAsync(ct);

            return Results.Ok(new Paged<DatasetDto>(
                items, page, pageSize, total, (int)Math.Ceiling(total / (double)pageSize)));
        })
        .WithName("ListDatasets")
        .WithSummary("Paged, searchable, sortable list of datasets.");

        group.MapGet("/{id:int}", async (AppDbContext db, int id, CancellationToken ct) =>
        {
            var dataset = await db.Datasets.AsNoTracking()
                .Include(d => d.Columns)
                .FirstOrDefaultAsync(d => d.Id == id, ct);

            if (dataset is null) return Problems.NoDataset(id);

            return Results.Ok(new DatasetDetailDto(
                Map(dataset),
                dataset.Columns.OrderBy(c => c.Ordinal).Select(MapColumn).ToList()));
        })
        .WithName("GetDataset")
        .WithSummary("One dataset with its full column profile.");

        group.MapPost("/upload", async (AppDbContext db, DashboardService dashboards, IFormFile? file, CancellationToken ct) =>
        {
            if (file is null || file.Length == 0)
                return Problems.BadRequest(
                    "No file received",
                    "Attach a file to the 'file' field of a multipart/form-data request.");

            if (file.Length > InputLimits.UploadBytes)
                return Results.Problem(
                    title: "That file is too large",
                    detail: $"Files up to {InputLimits.UploadBytes / (1024 * 1024)} MB can be uploaded.",
                    statusCode: StatusCodes.Status413PayloadTooLarge);

            var extension = Path.GetExtension(file.FileName).ToLowerInvariant();
            if (extension is not (".csv" or ".tsv" or ".txt"))
                return Results.Problem(
                    title: "That file type is not supported yet",
                    detail: $"'{extension}' cannot be parsed. Upload a CSV, TSV or TXT file.",
                    statusCode: StatusCodes.Status415UnsupportedMediaType);

            // The file is read into memory once: parsed from that copy, and kept
            // compressed. Every screen reads its columns from that copy, so it
            // is stored as uploaded rather than mapped onto a fixed schema.
            byte[] raw;
            using (var buffer = new MemoryStream((int)file.Length))
            {
                await file.CopyToAsync(buffer, ct);
                raw = buffer.ToArray();
            }

            CsvProfiler.ParseResult parsed;
            using (var reader = new StreamReader(new MemoryStream(raw)))
                parsed = CsvProfiler.Parse(reader);

            if (parsed.Headers.Count == 0)
                return Results.Problem(
                    title: "The file has no header row",
                    detail: "The first line must name the columns.",
                    statusCode: StatusCodes.Status422UnprocessableEntity);

            var columns = CsvProfiler.Profile(parsed);

            var dataset = new Dataset
            {
                Name = InputLimits.Clip(Path.GetFileNameWithoutExtension(file.FileName), InputLimits.DatasetNameLength),
                Type = extension.TrimStart('.').ToUpperInvariant(),
                Icon = "table_view",
                RowCount = parsed.Rows.Count,
                ColumnCount = parsed.Headers.Count,
                Quality = CsvProfiler.QualityFor(columns, parsed.Rows.Count),
                UpdatedAt = DateTime.UtcNow,
                Columns = columns,
            };

            db.Datasets.Add(dataset);
            await db.SaveChangesAsync(ct);

            db.DatasetSources.Add(new DatasetSource { DatasetId = dataset.Id, Content = SourceStore.Compress(raw) });
            raw = [];
            await db.SaveChangesAsync(ct);
            db.ChangeTracker.Clear();

            // Written now, while the parsed file is in memory, so every screen
            // opens on a new upload without reading the file a second time.
            dashboards.Prime(dataset, parsed, columns);

            return Results.Created($"/api/datasets/{dataset.Id}", new UploadResultDto(
                Map(dataset),
                columns.Select(MapColumn).ToList(),
                dataset.RowCount));
        })
        .DisableAntiforgery()
        .WithName("UploadDataset")
        .WithSummary("Upload a delimited file; it is profiled, stored as uploaded and immediately analysed.");

        group.MapDelete("/{id:int}", async (AppDbContext db, int id, CancellationToken ct) =>
        {
            var dataset = await db.Datasets.FirstOrDefaultAsync(d => d.Id == id, ct);
            if (dataset is null) return Problems.NoDataset(id);

            // Everything about the file goes with it: its column profile, its
            // stored copy, the assistant's conversations about it and the reports
            // written from it. Reports name their file rather than point at it, so
            // they stay while another file of the same name can still answer them.
            var sessionIds = db.ChatSessions.Where(s => s.DatasetId == id).Select(s => s.Id);
            var sameName = await db.Datasets.AnyAsync(d => d.Id != id && d.Name == dataset.Name, ct);

            await using var tx = await db.Database.BeginTransactionAsync(ct);
            await db.ChatMessages.Where(m => sessionIds.Contains(m.SessionId)).ExecuteDeleteAsync(ct);
            await db.ChatSessions.Where(s => s.DatasetId == id).ExecuteDeleteAsync(ct);
            if (!sameName)
                await db.Reports.Where(r => r.DatasetName == dataset.Name).ExecuteDeleteAsync(ct);
            db.Datasets.Remove(dataset);
            await db.SaveChangesAsync(ct);
            await db.UserSettings.Where(u => u.ActiveDatasetId == id)
                .ExecuteUpdateAsync(u => u.SetProperty(x => x.ActiveDatasetId, (int?)null), ct);
            await tx.CommitAsync(ct);

            return Results.NoContent();
        })
        .WithName("DeleteDataset")
        .WithSummary("Delete a dataset with its column profile, stored copy, assistant conversations and reports.");

        return api;
    }

    internal static DatasetDto Map(Dataset d) =>
        new(d.Id, d.Name, d.Type, d.Icon, d.RowCount, d.ColumnCount, d.Quality, d.UpdatedAt);

    internal static ColumnProfileDto MapColumn(DatasetColumn c) =>
        new(c.Name, c.Ordinal, c.Kind, c.Missing, c.Distinct, c.Min, c.Max, c.Mean, c.StdDev,
            c.SampleValues.Length == 0 ? [] : c.SampleValues.Split(','));
}
