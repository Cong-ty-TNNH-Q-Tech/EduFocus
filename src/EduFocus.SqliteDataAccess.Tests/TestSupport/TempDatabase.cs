using EduFocus.SqliteDataAccess.Persistence;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;

namespace EduFocus.SqliteDataAccess.Tests.TestSupport;

internal sealed class TempDatabase : IAsyncDisposable
{
    public TempDatabase()
    {
        RootPath = Path.Combine(Path.GetTempPath(), "edufocus-tests", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(RootPath);

        DatabasePath = Path.Combine(RootPath, "edufocus.db");
        BackupDirectory = Path.Combine(RootPath, "backups");

        Options = new EduFocusDatabaseOptions
        {
            DatabasePath = DatabasePath,
            BackupDirectory = BackupDirectory,
            ProtectDataDirectory = false
        };
        KeyStore = new DatabaseKeyStore(Options);
        ContextFactory = new SqliteDbContextFactory(Options, KeyStore);
    }

    public string RootPath { get; }

    public string DatabasePath { get; }

    public string BackupDirectory { get; }

    public RecordingLogger Logger { get; } = new();

    public EduFocusDatabaseOptions Options { get; }

    public DatabaseKeyStore KeyStore { get; }

    public SqliteDbContextFactory ContextFactory { get; }

    public SqliteDatabaseInitializer CreateInitializer(TimeProvider? timeProvider = null) =>
        new(Options, Logger, KeyStore, timeProvider);

    public SqliteBackupStore CreateBackupStore(TimeProvider? timeProvider = null) =>
        new(Options, Logger, KeyStore, timeProvider);

    public EduFocusDbContext CreateContext() => ContextFactory.CreateDbContext();

    /// <summary>Context for creating a pre-encryption database in upgrade tests.</summary>
    public EduFocusDbContext CreatePlaintextContext()
    {
        var builder = new DbContextOptionsBuilder<EduFocusDbContext>()
            .UseSqlite(Options.ConnectionString);

        return new EduFocusDbContext(builder.Options);
    }

    public SqliteConnection OpenConnection()
    {
        var encrypted = SqliteCipher.GetFileState(DatabasePath) == SqliteDatabaseFileState.Encrypted;
        var connection = new SqliteConnection(Options.ConnectionString);
        connection.Open();

        if (encrypted)
            KeyStore.Get().ApplyTo(connection);

        return connection;
    }

    public SqliteConnection OpenPlaintextConnection()
    {
        var connection = new SqliteConnection(Options.ConnectionString);
        connection.Open();
        return connection;
    }

    public SqliteConnection OpenConnectionWithPragmas()
    {
        var connection = OpenConnection();
        ExecuteNonQuery(connection, "PRAGMA foreign_keys = ON; PRAGMA journal_mode = WAL;");
        return connection;
    }

    public static object? Scalar(SqliteConnection connection, string sql)
    {
        using var command = connection.CreateCommand();
        command.CommandText = sql;
        return command.ExecuteScalar();
    }

    public static List<string> QueryStrings(SqliteConnection connection, string sql)
    {
        using var command = connection.CreateCommand();
        command.CommandText = sql;

        var values = new List<string>();
        using var reader = command.ExecuteReader();
        while (reader.Read())
            values.Add(reader.GetString(0));

        return values;
    }

    public static void ExecuteNonQuery(SqliteConnection connection, string sql)
    {
        using var command = connection.CreateCommand();
        command.CommandText = sql;
        command.ExecuteNonQuery();
    }

    public static List<string> GetTableNames(SqliteConnection connection) =>
        QueryStrings(
            connection,
            "SELECT name FROM sqlite_master WHERE type = 'table' AND name NOT LIKE 'sqlite_%' ORDER BY name;");

    public static List<string> GetIndexNames(SqliteConnection connection) =>
        QueryStrings(
            connection,
            "SELECT name FROM sqlite_master WHERE type = 'index' AND name NOT LIKE 'sqlite_autoindex%' ORDER BY name;");

    public async ValueTask DisposeAsync()
    {
        SqliteConnection.ClearAllPools();
        await Task.Yield();

        try
        {
            Directory.Delete(RootPath, recursive: true);
        }
        catch (IOException)
        {
        }
        catch (UnauthorizedAccessException)
        {
        }
    }
}
