using EduFocus.Ports.Outbound.Services;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;

namespace EduFocus.SqliteDataAccess.Persistence;

public sealed record DatabaseInitializationResult(
    bool WasCreated,
    IReadOnlyList<string> AppliedMigrations,
    string? BackupPath);

/// <summary>
/// Creates and protects the data directory, verifies the existing database,
/// backs up before migrations (verified copy, newest three kept), applies EF
/// Core migrations and configures SQLite pragmas. Failures keep the existing
/// file and surface a safe exception instead of deleting data
/// (database_schema §14, §16, §17).
/// </summary>
public sealed class SqliteDatabaseInitializer
{
    private readonly EduFocusDatabaseOptions _options;
    private readonly IAppLogger _logger;
    private readonly DbContextOptions<EduFocusDbContext> _dbContextOptions;
    private readonly SqliteBackupStore _backupStore;

    public SqliteDatabaseInitializer(
        EduFocusDatabaseOptions options,
        IAppLogger logger,
        TimeProvider? timeProvider = null)
    {
        ArgumentNullException.ThrowIfNull(options);
        ArgumentNullException.ThrowIfNull(logger);

        _options = options;
        _logger = logger;
        _dbContextOptions = new DbContextOptionsBuilder<EduFocusDbContext>()
            .UseSqlite(options.ConnectionString)
            .AddInterceptors(new SqlitePragmaInterceptor())
            .Options;
        _backupStore = new SqliteBackupStore(options, logger, timeProvider);
    }

    public async Task<DatabaseInitializationResult> InitializeAsync(CancellationToken cancellationToken = default)
    {
        EnsureDataDirectory();

        var existed = File.Exists(_options.DatabasePath)
            && new FileInfo(_options.DatabasePath).Length > 0;

        if (existed)
            EnsureDatabaseIsHealthy();

        if (!existed)
            ConfigureNewDatabaseSafely();

        string? backupPath = null;
        List<string> pending;

        try
        {
            await using var context = new EduFocusDbContext(_dbContextOptions);

            pending = (await context.Database.GetPendingMigrationsAsync(cancellationToken))
                .ToList();

            if (pending.Count == 0)
            {
                _logger.Info(LogCategories.Database, $"Database schema is up to date at '{_options.DatabasePath}'.");
                return new DatabaseInitializationResult(false, [], null);
            }

            if (existed)
            {
                backupPath = _backupStore.CreateBackup();
                EnsureWalMode();
            }

            _logger.Info(
                LogCategories.Database,
                $"Applying {pending.Count} migration(s) to '{_options.DatabasePath}': {string.Join(", ", pending)}.");
            await context.Database.MigrateAsync(cancellationToken);
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (SqliteBackupException)
        {
            _logger.Error(
                LogCategories.Database,
                "The pre-migration database backup failed, so no migration was started. The existing database was kept.");
            throw;
        }
        catch (Exception ex)
        {
            _logger.Error(
                LogCategories.Database,
                $"Database migration failed for '{_options.DatabasePath}'. The existing database was kept and no data was deleted.",
                ex);

            if (backupPath is not null)
                _logger.Warning(LogCategories.Database, $"Restore the database from the backup at '{backupPath}' if needed.");

            throw new SqliteMigrationException(
                "Database migration failed. The existing database was kept; restore from the backup if necessary.",
                ex,
                backupPath);
        }

        _logger.Info(LogCategories.Database, $"Database schema initialized at '{_options.DatabasePath}'.");
        return new DatabaseInitializationResult(!existed, pending, backupPath);
    }

    /// <summary>
    /// Restores a verified backup over the current database. Stop EduFocus
    /// first; a pre-restore snapshot is created when possible and the current
    /// file is left untouched when the backup is missing or corrupt.
    /// </summary>
    public string? RestoreBackup(string backupPath) => _backupStore.Restore(backupPath);

    private void EnsureDataDirectory()
    {
        var directory = Path.GetDirectoryName(Path.GetFullPath(_options.DatabasePath));
        if (string.IsNullOrEmpty(directory))
            return;

        try
        {
            Directory.CreateDirectory(directory);

            if (!_options.ProtectDataDirectory)
                return;

            DataDirectorySecurity.Apply(directory);
            _logger.Info(LogCategories.Database, $"Data directory restricted to Administrators and SYSTEM: '{directory}'.");
        }
        catch (Exception ex) when (ex is UnauthorizedAccessException or IOException
            or DirectoryNotFoundException or PlatformNotSupportedException)
        {
            _logger.Error(
                LogCategories.Database,
                $"Could not prepare or restrict the data directory '{directory}'. No data was deleted.",
                ex);

            throw new DataDirectorySecurityException(
                "EduFocus could not prepare the data folder. Run EduFocus as an administrator; existing data was not modified.",
                ex);
        }
    }

    private void EnsureDatabaseIsHealthy()
    {
        if (_backupStore.VerifyIntegrity(_options.DatabasePath))
            return;

        var backupDirectory = _options.ResolvedBackupDirectory;

        _logger.Error(
            LogCategories.Database,
            $"Database integrity check failed for '{_options.DatabasePath}'. The file was not modified and no data was deleted.");
        _logger.Warning(LogCategories.Database, $"Restore a verified backup from '{backupDirectory}' before using EduFocus again.");

        throw new SqliteDatabaseCorruptException(
            "The EduFocus database failed the integrity check. The file was not modified; restore a backup before continuing.",
            backupDirectory);
    }

    /// <summary>auto_vacuum only takes effect before the first table is created.</summary>
    private void ConfigureNewDatabaseSafely()
    {
        try
        {
            ConfigureNewDatabase();
        }
        catch (Exception ex) when (ex is SqliteException or IOException or UnauthorizedAccessException)
        {
            _logger.Error(
                LogCategories.Database,
                $"Could not create the database at '{_options.DatabasePath}'. Check folder permissions and free disk space.",
                ex);

            throw new DataDirectorySecurityException(
                "EduFocus could not create the database. Run EduFocus as an administrator and check free disk space; no data was deleted.",
                ex);
        }
    }

    private void ConfigureNewDatabase()
    {
        using var connection = new SqliteConnection(_options.ConnectionString);
        connection.Open();

        var currentMode = Convert.ToInt64(
            ExecuteScalar(connection, "PRAGMA auto_vacuum;"));

        if (currentMode != 2)
        {
            ExecuteNonQuery(connection, "PRAGMA auto_vacuum = INCREMENTAL;");
            ExecuteNonQuery(connection, "VACUUM;");
            _logger.Info(LogCategories.Database, "Configured incremental auto-vacuum for the new database.");
        }

        ExecuteNonQuery(connection, "PRAGMA journal_mode = WAL;");
    }

    private void EnsureWalMode()
    {
        using var connection = new SqliteConnection(_options.ConnectionString);
        connection.Open();
        ExecuteNonQuery(connection, "PRAGMA journal_mode = WAL;");
    }

    private static object? ExecuteScalar(SqliteConnection connection, string sql)
    {
        using var command = connection.CreateCommand();
        command.CommandText = sql;
        return command.ExecuteScalar();
    }

    private static void ExecuteNonQuery(SqliteConnection connection, string sql)
    {
        using var command = connection.CreateCommand();
        command.CommandText = sql;
        command.ExecuteNonQuery();
    }
}
