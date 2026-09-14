using AnalystAI.Api.Contracts;
using AnalystAI.Api.Data;
using AnalystAI.Api.Models;
using AnalystAI.Api.Security;
using AnalystAI.Api.Services;
using Microsoft.EntityFrameworkCore;

namespace AnalystAI.Api.Endpoints;

public static class DatasetEndpoints
{
    /// <summary>Rows are inserted in batches so a large file does not build one enormous command.</summary>
    private const int InsertBatch = 2_000;

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

        group.MapPost("/upload", async (AppDbContext db, IFormFile? file, CancellationToken ct) =>
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

            using var reader = new StreamReader(file.OpenReadStream());
            var parsed = CsvProfiler.Parse(reader);

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

            // Profiling alone left an uploaded file unqueryable. The rows are
            // mapped onto the queryable schema and stored, so every other screen
            // works against an upload exactly as it does against the seed file.
            //
            // They are mapped a batch at a time, and each parsed row is let go
            // once it is stored. Mapping the whole file up front held a second
            // full copy of it in memory, next to the parsed one.
            var plan = RowMapper.PlanFor(parsed.Headers);
            var stored = parsed.Rows.Count;
            var batch = new List<SalesRow>(InsertBatch);

            for (var i = 0; i < parsed.Rows.Count; i++)
            {
                var row = RowMapper.MapRow(parsed.Rows[i], plan);
                row.DatasetId = dataset.Id;
                batch.Add(row);
                parsed.Rows[i] = null!;

                if (batch.Count < InsertBatch && i < parsed.Rows.Count - 1) continue;

                db.SalesRows.AddRange(batch);
                await db.SaveChangesAsync(ct);

                // Saved rows are never read back here. Letting them pile up in
                // the change tracker made a large file cost memory in proportion
                // to its size and slowed every later batch.
                db.ChangeTracker.Clear();
                batch.Clear();
            }

            return Results.Created($"/api/datasets/{dataset.Id}", new UploadResultDto(
                Map(dataset),
                columns.Select(MapColumn).ToList(),
                stored,
                plan.Mapping.Select(m => new FieldMappingDto(m.Field, m.Header)).ToList()));
        })
        .DisableAntiforgery()
        .WithName("UploadDataset")
        .WithSummary("Upload a delimited file; it is profiled, stored and immediately queryable.");

        group.MapDelete("/{id:int}", async (AppDbContext db, int id, CancellationToken ct) =>
        {
            var dataset = await db.Datasets.FirstOrDefaultAsync(d => d.Id == id, ct);
            if (dataset is null) return Problems.NoDataset(id);

            // The last file used to be undeletable, on the grounds that every
            // screen needs rows to read. That was true when the database
            // arrived pre-filled; now that it starts empty, an empty library is
            // a state the app is built for, and refusing to delete someone's
            // only file trapped their data in the product.

            // SalesRow has no navigation back to Dataset, so its rows would
            // otherwise survive the file they belong to.
            var rows = db.SalesRows.Where(r => r.DatasetId == id);
            db.SalesRows.RemoveRange(rows);
            db.Datasets.Remove(dataset);
            await db.SaveChangesAsync(ct);

            return Results.NoContent();
        })
        .WithName("DeleteDataset")
        .WithSummary("Delete a dataset with its column profile and its stored rows.");

        return api;
    }

    internal static DatasetDto Map(Dataset d) =>
        new(d.Id, d.Name, d.Type, d.Icon, d.RowCount, d.ColumnCount, d.Quality, d.UpdatedAt);

    internal static ColumnProfileDto MapColumn(DatasetColumn c) =>
        new(c.Name, c.Ordinal, c.Kind, c.Missing, c.Distinct, c.Min, c.Max, c.Mean, c.StdDev,
            c.SampleValues.Length == 0 ? [] : c.SampleValues.Split(','));
}
