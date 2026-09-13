using Npgsql;

namespace AnalystAI.Api.Data;

/// <summary>Reads which database a connection string points at.</summary>
public static class DatabaseConnection
{
    public static bool IsPostgres(string connectionString) =>
        connectionString.StartsWith("postgres://", StringComparison.OrdinalIgnoreCase)
        || connectionString.StartsWith("postgresql://", StringComparison.OrdinalIgnoreCase)
        || connectionString.Contains("Host=", StringComparison.OrdinalIgnoreCase);

    /// <summary>
    /// Npgsql takes key=value connection strings, while hosts such as Render hand
    /// out a URL: postgresql://user:password@host:port/database. A URL is
    /// converted; a key=value string passes through unchanged.
    /// </summary>
    public static string ToNpgsql(string connectionString)
    {
        if (!connectionString.Contains("://", StringComparison.Ordinal)) return connectionString;

        var uri = new Uri(connectionString);
        var credentials = uri.UserInfo.Split(':', 2);

        var builder = new NpgsqlConnectionStringBuilder
        {
            Host = uri.Host,
            Port = uri.Port > 0 ? uri.Port : 5432,
            Username = Uri.UnescapeDataString(credentials[0]),
            Password = credentials.Length > 1 ? Uri.UnescapeDataString(credentials[1]) : null,
            Database = Uri.UnescapeDataString(uri.AbsolutePath.TrimStart('/')),
            SslMode = SslMode.Prefer,
        };

        // An explicit sslmode in the URL wins over the default.
        foreach (var pair in uri.Query.TrimStart('?').Split('&', StringSplitOptions.RemoveEmptyEntries))
        {
            var parts = pair.Split('=', 2);
            if (parts.Length == 2
                && parts[0].Equals("sslmode", StringComparison.OrdinalIgnoreCase)
                && Enum.TryParse<SslMode>(parts[1].Replace("-", ""), ignoreCase: true, out var mode))
            {
                builder.SslMode = mode;
            }
        }

        return builder.ConnectionString;
    }
}
