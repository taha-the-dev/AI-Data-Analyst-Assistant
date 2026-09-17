using System.IO.Compression;
using AnalystAI.Api.Analysis;
using AnalystAI.Api.Contracts;
using AnalystAI.Api.Data;
using AnalystAI.Api.Models;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Caching.Memory;

namespace AnalystAI.Api.Services;

/// <summary>
/// Keeps each uploaded file and writes its dashboard from it.
///
/// Reading and analysing a large file is the most expensive thing the service
/// does, and the result only changes when the file does, so it is worked out
/// once — at upload, while the parsed file is still in memory — and cached. A
/// restart or an evicted entry costs one re-read of the stored copy.
/// </summary>
public sealed class DashboardService(AppDbContext db, IMemoryCache cache)
{
    private static readonly MemoryCacheEntryOptions CacheOptions = new() { SlidingExpiration = TimeSpan.FromMinutes(30) };

    public static byte[] Compress(byte[] raw)
    {
        using var output = new MemoryStream();
        using (var gzip = new GZipStream(output, CompressionLevel.Fastest, leaveOpen: true))
            gzip.Write(raw);
        return output.ToArray();
    }

    /// <summary>Analyses a file that has just been parsed and stored, so its first view is instant.</summary>
    public void Prime(Dataset dataset, CsvProfiler.ParseResult parsed, IReadOnlyList<DatasetColumn> columns) =>
        cache.Set(Key(dataset), DatasetAnalyzer.Analyze(parsed, columns), CacheOptions);

    /// <summary>
    /// The dashboard for a dataset the caller has already loaded, which is what
    /// proves the signed-in account owns it. Null when no copy of the file was
    /// kept — it was uploaded before copies were.
    /// </summary>
    public async Task<DashboardDto?> GetAsync(Dataset dataset, CancellationToken ct)
    {
        if (cache.TryGetValue(Key(dataset), out DashboardDto? cached) && cached is not null) return cached;

        var content = await db.DatasetSources.AsNoTracking()
            .Where(s => s.DatasetId == dataset.Id)
            .Select(s => s.Content)
            .FirstOrDefaultAsync(ct);
        if (content is null) return null;

        CsvProfiler.ParseResult parsed;
        await using (var gzip = new GZipStream(new MemoryStream(content), CompressionMode.Decompress))
        using (var reader = new StreamReader(gzip))
            parsed = CsvProfiler.Parse(reader);

        var dashboard = DatasetAnalyzer.Analyze(parsed, CsvProfiler.Profile(parsed));
        cache.Set(Key(dataset), dashboard, CacheOptions);
        return dashboard;
    }

    /// <summary>
    /// Keyed on the update time too, so a replaced file never reads a stale
    /// dashboard. Microseconds, because PostgreSQL stores no finer.
    /// </summary>
    private static string Key(Dataset dataset) => $"dashboard:{dataset.Id}:{dataset.UpdatedAt.Ticks / 10}";
}
