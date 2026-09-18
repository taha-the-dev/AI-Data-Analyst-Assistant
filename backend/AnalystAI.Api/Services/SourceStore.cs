using System.IO.Compression;
using AnalystAI.Api.Analysis;
using AnalystAI.Api.Data;
using AnalystAI.Api.Models;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Caching.Memory;

namespace AnalystAI.Api.Services;

/// <summary>
/// The uploaded file, read back column by column.
///
/// Every screen — Dashboard, Analytics, the Explorer, the assistant and
/// reports — works from the file's own columns, so they share one parsed copy.
/// Re-reading a large file is the expensive part; the parsed frame is read-only
/// once built, so one cached copy serves every request for that file.
/// </summary>
internal sealed class SourceStore(AppDbContext db, IMemoryCache cache)
{
    /// <summary>
    /// The cache's capacity, in cells. A cell costs roughly 60 bytes held as a
    /// frame, so this keeps parsed files to about 250 MB however many are open.
    /// </summary>
    public const long CacheCells = 4_000_000;

    private static MemoryCacheEntryOptions OptionsFor(Frame frame) => new()
    {
        SlidingExpiration = TimeSpan.FromMinutes(10),
        AbsoluteExpirationRelativeToNow = TimeSpan.FromMinutes(30),
        Size = Math.Max(1, (long)frame.RowCount * Math.Max(1, frame.Columns.Count)),
    };

    public static byte[] Compress(byte[] raw)
    {
        using var output = new MemoryStream();
        using (var gzip = new GZipStream(output, CompressionLevel.Fastest, leaveOpen: true))
            gzip.Write(raw);
        return output.ToArray();
    }

    /// <summary>Holds a file that has just been parsed at upload, so its first screens need no re-read.</summary>
    public Frame Prime(Dataset dataset, CsvProfiler.ParseResult parsed, IReadOnlyList<DatasetColumn> columns)
    {
        var frame = Frame.From(parsed, columns);
        cache.Set(Key(dataset), frame, OptionsFor(frame));
        return frame;
    }

    /// <summary>
    /// The frame for a dataset the caller has already loaded — which is what
    /// proves the signed-in account owns it. Null when no copy of the file was
    /// kept: it was uploaded before copies were.
    /// </summary>
    public async Task<Frame?> LoadAsync(Dataset dataset, CancellationToken ct)
    {
        if (cache.TryGetValue(Key(dataset), out Frame? cached) && cached is not null) return cached;

        var content = await db.DatasetSources.AsNoTracking()
            .Where(s => s.DatasetId == dataset.Id)
            .Select(s => s.Content)
            .FirstOrDefaultAsync(ct);
        if (content is null) return null;

        CsvProfiler.ParseResult parsed;
        await using (var gzip = new GZipStream(new MemoryStream(content), CompressionMode.Decompress))
        using (var reader = new StreamReader(gzip))
            parsed = CsvProfiler.Parse(reader);

        var frame = Frame.From(parsed, CsvProfiler.Profile(parsed));
        cache.Set(Key(dataset), frame, OptionsFor(frame));
        return frame;
    }

    /// <summary>
    /// Keyed on the update time too, so a replaced file is never read stale.
    /// Microseconds, because PostgreSQL stores no finer.
    /// </summary>
    internal static string Key(Dataset dataset, string what = "frame") => $"{what}:{dataset.Id}:{dataset.UpdatedAt.Ticks / 10}";
}
