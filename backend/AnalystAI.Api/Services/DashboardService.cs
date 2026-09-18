using AnalystAI.Api.Analysis;
using AnalystAI.Api.Contracts;
using AnalystAI.Api.Models;
using Microsoft.Extensions.Caching.Memory;

namespace AnalystAI.Api.Services;

/// <summary>
/// Writes each file's dashboard from its stored copy.
///
/// The result only changes when the file does, so it is worked out once — at
/// upload, while the parsed file is still in memory — and cached.
/// </summary>
internal sealed class DashboardService(SourceStore sources, IMemoryCache cache)
{
    private static readonly MemoryCacheEntryOptions CacheOptions = new() { SlidingExpiration = TimeSpan.FromMinutes(30) };

    /// <summary>Parses into the shared frame and analyses it, so the first view of a new upload is instant.</summary>
    public void Prime(Dataset dataset, CsvProfiler.ParseResult parsed, IReadOnlyList<DatasetColumn> columns)
    {
        var frame = sources.Prime(dataset, parsed, columns);
        cache.Set(SourceStore.Key(dataset, "dashboard"), DatasetAnalyzer.Analyze(frame), CacheOptions);
    }

    /// <summary>Null when no copy of the file was kept.</summary>
    public async Task<DashboardDto?> GetAsync(Dataset dataset, CancellationToken ct)
    {
        var key = SourceStore.Key(dataset, "dashboard");
        if (cache.TryGetValue(key, out DashboardDto? cached) && cached is not null) return cached;

        var frame = await sources.LoadAsync(dataset, ct);
        if (frame is null) return null;

        var dashboard = DatasetAnalyzer.Analyze(frame);
        cache.Set(key, dashboard, CacheOptions);
        return dashboard;
    }
}
