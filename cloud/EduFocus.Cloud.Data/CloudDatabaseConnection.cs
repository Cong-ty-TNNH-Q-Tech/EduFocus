using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;

namespace EduFocus.Cloud.Data;

public static class CloudDatabaseConnection
{
    public const string EnvironmentVariable = "ConnectionStrings__Sqlite";
    public const string DevelopmentFallback =
        "Data Source=edufocus-cloud.db;Foreign Keys=True;Default Timeout=30";

    public static string Resolve(string? configured, string? environmentName)
    {
        var value = configured ?? Environment.GetEnvironmentVariable(EnvironmentVariable);
        if (!string.IsNullOrWhiteSpace(value))
        {
            return value;
        }

        if (string.Equals(environmentName, "Development", StringComparison.OrdinalIgnoreCase))
        {
            return DevelopmentFallback;
        }

        throw new InvalidOperationException(
            $"SQLite connection string is required. Set {EnvironmentVariable} or ConnectionStrings:Sqlite.");
    }

    /// <summary>
    /// Enables WAL journal mode and normal synchronous durability on writable file-backed SQLite databases.
    /// </summary>
    public static async Task InitializeWalAsync(
        CloudDbContext db,
        string connectionString,
        CancellationToken cancellationToken = default)
    {
        var builder = new SqliteConnectionStringBuilder(connectionString);
        if (builder.Mode == SqliteOpenMode.ReadOnly
            || string.IsNullOrWhiteSpace(builder.DataSource)
            || string.Equals(builder.DataSource, ":memory:", StringComparison.OrdinalIgnoreCase))
        {
            return;
        }

        if (builder.Mode == SqliteOpenMode.ReadWrite && !File.Exists(builder.DataSource))
        {
            return;
        }

        await db.Database.OpenConnectionAsync(cancellationToken);
        try
        {
            await db.Database.ExecuteSqlRawAsync(
                "PRAGMA journal_mode = WAL; PRAGMA synchronous = NORMAL;",
                cancellationToken);
        }
        finally
        {
            await db.Database.CloseConnectionAsync();
        }
    }
}
