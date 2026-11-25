using System.Runtime.CompilerServices;
using EduFocus.Cloud.Data;
using Microsoft.EntityFrameworkCore;

namespace EduFocus.Cloud.Tests;

internal static class TestDatabase
{
    public const string EnvironmentVariable = "EDUFOCUS_CLOUD_TEST_DB";

    public static string ConnectionString { get; } = CreateConnectionString();

    [ModuleInitializer]
    public static void Initialize()
    {
        Environment.SetEnvironmentVariable(EnvironmentVariable, ConnectionString);
        AppDomain.CurrentDomain.ProcessExit += (_, _) => DeleteFiles();
    }

    public static CloudDbContext CreateContext(string? connectionString = null) =>
        new(new DbContextOptionsBuilder<CloudDbContext>()
            .UseSqlite(connectionString ?? ConnectionString)
            .Options);

    public static async Task ResetAsync(string? connectionString = null)
    {
        await using var db = CreateContext(connectionString);
        await db.Database.EnsureDeletedAsync();
        await db.Database.MigrateAsync();
    }

    private static string CreateConnectionString()
    {
        var directory = Path.Combine(Path.GetTempPath(), "edufocus-cloud-tests");
        Directory.CreateDirectory(directory);
        return $"Data Source={Path.Combine(directory, $"cloud-{Guid.NewGuid():N}.db")};" +
            "Foreign Keys=True;Default Timeout=30";
    }

    private static void DeleteFiles()
    {
        var dataSource = new Microsoft.Data.Sqlite.SqliteConnectionStringBuilder(ConnectionString)
            .DataSource;
        foreach (var suffix in new[] { string.Empty, "-wal", "-shm" })
        {
            try
            {
                File.Delete(dataSource + suffix);
            }
            catch (IOException)
            {
            }
            catch (UnauthorizedAccessException)
            {
            }
        }
    }
}
