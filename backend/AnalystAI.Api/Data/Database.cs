using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;

namespace AnalystAI.Api.Data;

/// <summary>
/// Brings the database up to the current schema on startup.
///
/// The schema is managed by migrations, one set per database provider. A SQLite
/// file created before accounts existed was built by EnsureCreated, has no
/// migration history, and holds rows that belong to nobody — there is no
/// account to hand them to. Such a file is moved aside, never deleted, and a
/// fresh database is created in its place. Nothing is seeded: no sample rows,
/// no default account.
/// </summary>
public static class Database
{
    public static async Task PrepareAsync(AppDbContext db, ILogger logger, CancellationToken ct = default)
    {
        if (db.Database.IsSqlite())
            await MoveAsidePreAccountsFileAsync(db, logger, ct);

        await db.Database.MigrateAsync(ct);
    }

    private static async Task MoveAsidePreAccountsFileAsync(AppDbContext db, ILogger logger, CancellationToken ct)
    {
        var file = SqliteFile(db);
        if (file is null || !File.Exists(file) || !await PredatesAccountsAsync(db, ct)) return;

        var backup = Path.Combine(
            Path.GetDirectoryName(file)!,
            $"{Path.GetFileNameWithoutExtension(file)}.pre-accounts-{DateTime.UtcNow:yyyyMMddHHmmss}{Path.GetExtension(file)}");

        // Pooled connections keep the file open on Windows.
        SqliteConnection.ClearAllPools();

        foreach (var suffix in new[] { "", "-wal", "-shm" })
            if (File.Exists(file + suffix))
                File.Move(file + suffix, backup + suffix);

        logger.LogWarning(
            "The database at {File} was created before accounts existed, so its rows belong to no account. " +
            "It has been moved to {Backup} and a new, empty database created. Nothing was deleted.",
            file, backup);
    }

    private static string? SqliteFile(AppDbContext db)
    {
        var builder = new SqliteConnectionStringBuilder(db.Database.GetConnectionString());
        var inMemory = builder.Mode == SqliteOpenMode.Memory
                       || string.IsNullOrWhiteSpace(builder.DataSource)
                       || builder.DataSource == ":memory:";

        return inMemory ? null : Path.GetFullPath(builder.DataSource);
    }

    private static async Task<bool> PredatesAccountsAsync(AppDbContext db, CancellationToken ct)
    {
        var connection = db.Database.GetDbConnection();
        await connection.OpenAsync(ct);

        try
        {
            await using var command = connection.CreateCommand();
            command.CommandText =
                "SELECT name FROM sqlite_master WHERE type = 'table' AND name IN ('Datasets', '__EFMigrationsHistory')";

            var tables = new HashSet<string>();
            await using (var reader = await command.ExecuteReaderAsync(ct))
            {
                while (await reader.ReadAsync(ct))
                    tables.Add(reader.GetString(0));
            }

            return tables.Contains("Datasets") && !tables.Contains("__EFMigrationsHistory");
        }
        finally
        {
            await connection.CloseAsync();
        }
    }
}
