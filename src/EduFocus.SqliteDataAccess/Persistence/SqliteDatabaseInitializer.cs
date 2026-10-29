using EduFocus.Ports.Outbound.Services;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;

namespace EduFocus.SqliteDataAccess.Persistence;

public sealed record DatabaseInitializationResult(
    bool WasCreated,
    IReadOnlyList<string> AppliedMigrations,
    string? BackupPath);

/// <summary>
/// Creates the database directory, backs up before migrations (keeping the newest
/// three), applies EF Core migrations and configures SQLite pragmas. A failed
/// migration keeps the existing file and surfaces <see cref="SqliteMigrationException"/>
/// instead of deleting data (database_schema §14, §17).
/// </summary>
public sealed class SqliteDatabaseInitializer
{
    private readonly EduFocusDatabaseOptions _options;
    private readonly IAppLogger _logger;
    private readonly TimeProvider _timeProvider;
    private readonly DbContextOptions<EduFocusDbContext> _dbContextOptions;

    public SqliteDatabaseInitializer(
        EduFocusDatabaseOptions options,
        IAppLogger logger,
        TimeProvider? timeProvider = null)
    {
        ArgumentNullException.ThrowIfNull(options);
        ArgumentNullException.ThrowIfNull(logger);

        _options = options;
        _logger = logger;
        _timeProvider = timeProvider ?? TimeProvider.System;
        _dbContextOptions = new DbContextOptionsBuilder<EduFocusDbContext>()
            .UseSqlite(options.ConnectionString)
            .AddInterceptors(new SqlitePragmaInterceptor())
            .Options;
    }

    public async Task<DatabaseInitializationResult> InitializeAsync(CancellationToken cancellationToken = default)
    {
        EnsureDatabaseDirectory();

        var existed = File.Exists(_options.DatabasePath)
            && new FileInfo(_options.DatabasePath).Length > 0;

        if (!existed)
            ConfigureNewDatabase();

        string? backupPath = null;
        List<string> pending;

        try
        {
            await using var context = new EduFocusDbContext(_dbContextOptions);

            pending = (await context.Database.GetPendingMigrationsAsync(cancellationToken))
                .ToList();

            if (pending.Count == 0)
            {
                _logger.Info($"Database schema is up to date at '{_options.DatabasePath}'.");
                return new DatabaseInitializationResult(false, [], null);
            }

            if (existed)
            {
                backupPath = CreateBackup();
                EnsureWalMode();
            }

            _logger.Info(
                $"Applying {pending.Count} migration(s) to '{_options.DatabasePath}': {string.Join(", ", pending)}.");
            await context.Database.MigrateAsync(cancellationToken);
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (Exception ex)
        {
            _logger.Error(
                $"Database migration failed for '{_options.DatabasePath}'. The existing database was kept and no data was deleted.",
                ex);

            if (backupPath is not null)
                _logger.Warning($"Restore the database from the backup at '{backupPath}' if needed.");

            throw new SqliteMigrationException(
                "Database migration failed. The existing database was kept; restore from the backup if necessary.",
                ex,
                backupPath);
        }

        _logger.Info($"Database schema initialized at '{_options.DatabasePath}'.");
        return new DatabaseInitializationResult(!existed, pending, backupPath);
    }

    private void EnsureDatabaseDirectory()
    {
        var directory = Path.GetDirectoryName(Path.GetFullPath(_options.DatabasePath));
        if (string.IsNullOrEmpty(directory))
            return;

        Directory.CreateDirectory(directory);
    }

    /// <summary>auto_vacuum only takes effect before the first table is created.</summary>
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
            _logger.Info("Configured incremental auto-vacuum for the new database.");
        }

        ExecuteNonQuery(connection, "PRAGMA journal_mode = WAL;");
    }

    private void EnsureWalMode()
    {
        using var connection = new SqliteConnection(_options.ConnectionString);
        connection.Open();
        ExecuteNonQuery(connection, "PRAGMA journal_mode = WAL;");
    }

    private string CreateBackup()
    {
        var backupDirectory = _options.ResolvedBackupDirectory;
        Directory.CreateDirectory(backupDirectory);

        var fileName = $"edufocus-{_timeProvider.GetUtcNow():yyyyMMdd-HHmmssfff}.db";
        var backupPath = Path.Combine(backupDirectory, fileName);

        using var source = new SqliteConnection(_options.ConnectionString);
        using var destination = new SqliteConnection(
            new SqliteConnectionStringBuilder
            {
                DataSource = backupPath,
                Mode = SqliteOpenMode.ReadWriteCreate
            }.ToString());

        source.Open();
        destination.Open();
        source.BackupDatabase(destination);

        _logger.Info($"Database backup created at '{backupPath}'.");
        ApplyBackupRetention(backupDirectory);
        return backupPath;
    }

    private void ApplyBackupRetention(string backupDirectory)
    {
        var keep = Math.Max(1, _options.MaxBackups);

        var backups = Directory
            .EnumerateFiles(backupDirectory, "edufocus-*.db")
            .OrderByDescending(path => path, StringComparer.OrdinalIgnoreCase)
            .ToList();

        foreach (var oldBackup in backups.Skip(keep))
        {
            try
            {
                File.Delete(oldBackup);
                _logger.Info($"Removed old database backup '{oldBackup}'.");
            }
            catch (IOException ex)
            {
                _logger.Warning($"Could not remove old database backup '{oldBackup}': {ex.Message}");
            }
            catch (UnauthorizedAccessException ex)
            {
                _logger.Warning($"Could not remove old database backup '{oldBackup}': {ex.Message}");
            }
        }
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
