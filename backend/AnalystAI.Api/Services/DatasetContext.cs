using AnalystAI.Api.Data;
using Microsoft.EntityFrameworkCore;

namespace AnalystAI.Api.Services;

/// <summary>
/// Decides which dataset a request reads from.
///
/// Screens used to fall back to a constant id that only existed because the
/// seeder wrote it. Resolving against the database instead means the API keeps
/// working after that file is deleted, and that an id nobody uploaded is
/// reported as missing rather than silently swapped for another file's figures.
/// </summary>
public interface IDatasetContext
{
    /// <summary>
    /// Returns the dataset to use, or null when there is none. A requested id
    /// that does not exist resolves to null rather than to a different file.
    /// </summary>
    Task<int?> ResolveAsync(int? requested, CancellationToken ct = default);
}

public class DatasetContext(AppDbContext db) : IDatasetContext
{
    public async Task<int?> ResolveAsync(int? requested, CancellationToken ct = default)
    {
        if (requested is > 0)
            return await db.Datasets.AnyAsync(d => d.Id == requested, ct) ? requested : null;

        // No preference: the most recently updated file whose copy was kept,
        // since that is the only kind anything can be computed from.
        var withRows = await db.Datasets
            .Where(d => db.DatasetSources.Any(s => s.DatasetId == d.Id))
            .OrderByDescending(d => d.UpdatedAt)
            .Select(d => (int?)d.Id)
            .FirstOrDefaultAsync(ct);

        return withRows ?? await db.Datasets
            .OrderByDescending(d => d.UpdatedAt)
            .Select(d => (int?)d.Id)
            .FirstOrDefaultAsync(ct);
    }
}
