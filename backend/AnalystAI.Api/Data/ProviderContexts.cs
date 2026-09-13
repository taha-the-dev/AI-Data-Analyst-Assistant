using AnalystAI.Api.Services;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Design;

namespace AnalystAI.Api.Data;

/// <summary>
/// The model on SQLite: local development, and any host with a disk that
/// survives restarts. Its migrations are in Data/Migrations.
/// </summary>
public sealed class SqliteAppDbContext(DbContextOptions<SqliteAppDbContext> options, ICurrentUser currentUser)
    : AppDbContext(options, currentUser);

/// <summary>
/// The model on PostgreSQL: hosts whose disk is wiped on restart, such as a
/// free Render service. Its migrations are in Data/Migrations/Postgres.
/// </summary>
public sealed class PostgresAppDbContext(DbContextOptions<PostgresAppDbContext> options, ICurrentUser currentUser)
    : AppDbContext(options, currentUser);

/// <summary>Nobody is signed in while migrations are being generated.</summary>
public sealed class DesignTimeUser : ICurrentUser
{
    public string? Id => null;
}

/// <summary>Lets <c>dotnet ef</c> build the SQLite context without starting the app.</summary>
public sealed class SqliteDesignTimeFactory : IDesignTimeDbContextFactory<SqliteAppDbContext>
{
    public SqliteAppDbContext CreateDbContext(string[] args) => new(
        new DbContextOptionsBuilder<SqliteAppDbContext>().UseSqlite("Data Source=design-time.db").Options,
        new DesignTimeUser());
}

/// <summary>
/// Lets <c>dotnet ef</c> build the PostgreSQL context. Adding a migration never
/// connects, so the connection string only has to name the provider.
/// </summary>
public sealed class PostgresDesignTimeFactory : IDesignTimeDbContextFactory<PostgresAppDbContext>
{
    public PostgresAppDbContext CreateDbContext(string[] args) => new(
        new DbContextOptionsBuilder<PostgresAppDbContext>().UseNpgsql("Host=localhost;Database=design_time").Options,
        new DesignTimeUser());
}
