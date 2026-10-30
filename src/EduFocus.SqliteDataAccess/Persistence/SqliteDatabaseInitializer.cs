using EduFocus.Ports.Outbound.Services;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;

namespace EduFocus.SqliteDataAccess.Persistence;

public sealed record DatabaseInitializationResult(
    bool WasCreated,
    IReadOnlyList<string> AppliedMigrations,
    string? BackupPath);

/// <summary>
/// Creates and protects the data directory, unlocks or creates the SQLCipher
/// key, upgrades a legacy plaintext database, backs up before migrations
/// (verified copy, newest three kept), applies EF Core migrations and
/// configures SQLite pragmas. Failures keep the existing file and surface a
/// safe exception instead of deleting data (database_schema §14, §16, §17,
/// §18).
/// </summary>
public sealed class SqliteDatabaseInitializer
{
    private const string InitializationLockFileName = "edufocus.init.lock";
    private static readonly TimeSpan InitializationLockTimeout = TimeSpan.FromSeconds(30);

    private readonly EduFocusDatabaseOptions _options;
    private readonly IAppLogger _logger;
    private readonly IDatabaseKeyStore _keyStore;
    private readonly SqliteBackupStore _backupStore;
    private readonly SqliteDbContextFactory _contextFactory;

    public SqliteDatabaseInitializer(
        EduFocusDatabaseOptions options,
        IAppLogger logger,
        IDatabaseKeyStore? keyStore = null,
        TimeProvider? timeProvider = null)
    {
        ArgumentNullException.ThrowIfNull(options);
        ArgumentNullException.ThrowIfNull(logger);

        _options = options;
        _logger = logger;
        _keyStore = keyStore ?? new DatabaseKeyStore(options);
        _contextFactory = new SqliteDbContextFactory(options, _keyStore);
        _backupStore = new SqliteBackupStore(options, logger, _keyStore, timeProvider);
    }

    public async Task<DatabaseInitializationResult> InitializeAsync(CancellationToken cancellationToken = default)
    {
        EnsureDataDirectory();

        // The service and the UI can start at the same time after logon. A
        // cross-process file lock keeps both from applying migrations to the
        // same database concurrently; the lock file disappears on close and a
        // stale file from a crash is harmless.
        await using var initializationLock = await AcquireInitializationLockAsync(cancellationToken)
            .ConfigureAwait(false);

        var state = SqliteCipher.GetFileState(_options.DatabasePath);
        var existed = state is SqliteDatabaseFileState.Plaintext or SqliteDatabaseFileState.Encrypted;
        string? backupPath = null;

        if (state == SqliteDatabaseFileState.Plaintext)
        {
            _logger.Warning(
                LogCategories.Database,
                "The existing EduFocus database is plaintext. Encrypting it with SQLCipher; the original file is kept until the encrypted copy is verified.");
            backupPath = UpgradePlaintextDatabaseToEncrypted();
            state = SqliteDatabaseFileState.Encrypted;
        }

        if (state == SqliteDatabaseFileState.Encrypted)
        {
            var key = _keyStore.TryLoad();

            if (key is null)
            {
                _logger.Error(
                    LogCategories.Database,
                    $"The encrypted database '{_options.DatabasePath}' has no key file at '{_keyStore.KeyFilePath}'. No data was changed.");
                throw new DatabaseKeyException(
                    "The EduFocus database is encrypted but its key file is missing. Restore the key file together with the database backup, or contact support. No data was changed.",
                    _keyStore.KeyFilePath);
            }

            EnsureDatabaseIsHealthy();

            try
            {
                _backupStore.EncryptLegacyBackups(key);
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
            {
                _logger.Warning(
                    LogCategories.Database,
                    $"Legacy plaintext backups could not be encrypted on this start: {ex.Message}");
            }
        }
        else
        {
            _keyStore.LoadOrCreate();
            ConfigureNewDatabaseSafely();
        }

        List<string> pending;

        try
        {
            await using var context = _contextFactory.CreateDbContext();

            pending = (await context.Database.GetPendingMigrationsAsync(cancellationToken))
                .ToList();

            if (pending.Count == 0)
            {
                _logger.Info(LogCategories.Database, $"Database schema is up to date at '{_options.DatabasePath}'.");
                return new DatabaseInitializationResult(!existed, [], backupPath);
            }

            if (existed)
            {
                var preMigrationBackup = _backupStore.CreateBackup();
                backupPath ??= preMigrationBackup;
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
        catch (DatabaseKeyException)
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
    /// file is left untouched when the backup is missing or corrupt. A legacy
    /// plaintext backup is encrypted during the restore.
    /// </summary>
    public string? RestoreBackup(string backupPath) => _backupStore.Restore(backupPath);

    private async Task<FileStream> AcquireInitializationLockAsync(CancellationToken cancellationToken)
    {
        var directory = Path.GetDirectoryName(Path.GetFullPath(_options.DatabasePath))
            ?? throw new InvalidOperationException("The database path has no parent directory.");
        var lockPath = Path.Combine(directory, InitializationLockFileName);
        var deadline = DateTime.UtcNow.Add(InitializationLockTimeout);
        IOException? lastError = null;

        while (true)
        {
            cancellationToken.ThrowIfCancellationRequested();

            try
            {
                return new FileStream(
                    lockPath,
                    FileMode.OpenOrCreate,
                    FileAccess.ReadWrite,
                    FileShare.None,
                    bufferSize: 1,
                    FileOptions.DeleteOnClose);
            }
            catch (IOException ex) when (DateTime.UtcNow < deadline)
            {
                lastError = ex;
                await Task.Delay(250, cancellationToken).ConfigureAwait(false);
            }
            catch (IOException ex)
            {
                _logger.Error(
                    LogCategories.Database,
                    "Timed out waiting for another EduFocus process to finish database initialization.");
                throw new IOException(
                    "Timed out waiting for another EduFocus process to finish database initialization.",
                    lastError ?? ex);
            }
        }
    }

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
            $"Database integrity check failed or the stored key could not decrypt '{_options.DatabasePath}'. The file was not modified and no data was deleted.");
        _logger.Warning(LogCategories.Database, $"Restore a verified backup from '{backupDirectory}' before using EduFocus again.");

        throw new SqliteDatabaseCorruptException(
            "The EduFocus database could not be opened with the stored encryption key or failed the integrity check. The file was not modified; restore a backup before continuing.",
            backupDirectory);
    }

    /// <summary>
    /// Converts a plaintext database in place. The plaintext file is backed up
    /// first, the encrypted copy is verified, and only then is it moved over
    /// the original; a failure leaves the plaintext file untouched
    /// (database_schema §18).
    /// </summary>
    private string? UpgradePlaintextDatabaseToEncrypted()
    {
        var key = _keyStore.LoadOrCreate();
        string? backupPath;

        try
        {
            backupPath = _backupStore.CreateBackup();
        }
        catch (SqliteBackupException)
        {
            _logger.Error(
                LogCategories.Database,
                "The pre-encryption backup of the plaintext database failed, so the database was not converted. The existing database was kept.");
            throw;
        }

        var databasePath = Path.GetFullPath(_options.DatabasePath);
        var directory = Path.GetDirectoryName(databasePath)
            ?? throw new SqliteMigrationException(
                "Encrypting the EduFocus database failed because the database path is not valid.",
                new InvalidOperationException("The database path has no parent directory."),
                backupPath);
        var temporaryPath = Path.Combine(
            directory,
            $".{Path.GetFileName(databasePath)}.{Guid.NewGuid():N}.encrypting");

        try
        {
            using (var source = new SqliteConnection(
                BuildConnectionString(databasePath, SqliteOpenMode.ReadWriteCreate)))
            {
                source.Open();
                ExecuteNonQuery(source, "PRAGMA wal_checkpoint(TRUNCATE);");
                SqliteCipher.ExportEncrypted(source, temporaryPath, key);
            }

            if (!_backupStore.VerifyIntegrity(temporaryPath))
                throw new SqliteBackupException("The encrypted copy failed the integrity check.");

            SqliteConnection.ClearAllPools();
            DeleteSideFiles(databasePath);
            File.Move(temporaryPath, databasePath, overwrite: true);
            DeleteSideFiles(databasePath);
        }
        catch (Exception ex) when (ex is SqliteException or IOException or UnauthorizedAccessException
            or SqliteBackupException)
        {
            TryDelete(temporaryPath);
            _logger.Error(
                LogCategories.Database,
                $"Encrypting the existing database at '{_options.DatabasePath}' failed. The original database was kept and no data was deleted.",
                ex);

            throw new SqliteMigrationException(
                "Encrypting the existing database failed. The original database was kept; restore from the backup if necessary.",
                ex,
                backupPath);
        }

        try
        {
            EnsureWalMode();
        }
        catch (Exception ex) when (ex is SqliteException or IOException or UnauthorizedAccessException)
        {
            _logger.Warning(
                LogCategories.Database,
                $"The encrypted database was created but WAL mode could not be enabled: {ex.Message}");
        }

        _logger.Info(
            LogCategories.Database,
            $"The database at '{_options.DatabasePath}' is now encrypted with SQLCipher; the key is stored in '{_keyStore.KeyFilePath}'.");
        return backupPath;
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
        SqliteCipher.ApplyKey(connection, _keyStore.Get());

        var currentMode = Convert.ToInt64(
            ExecuteScalar(connection, "PRAGMA auto_vacuum;"));

        if (currentMode != 2)
        {
            ExecuteNonQuery(connection, "PRAGMA auto_vacuum = INCREMENTAL;");
            ExecuteNonQuery(connection, "VACUUM;");
            _logger.Info(LogCategories.Database, "Configured incremental auto-vacuum for the new encrypted database.");
        }

        ExecuteNonQuery(connection, "PRAGMA journal_mode = WAL;");
    }

    private void EnsureWalMode()
    {
        using var connection = new SqliteConnection(_options.ConnectionString);
        connection.Open();
        SqliteCipher.ApplyKey(connection, _keyStore.Get());
        ExecuteNonQuery(connection, "PRAGMA journal_mode = WAL;");
    }

    private static void DeleteSideFiles(string databasePath)
    {
        TryDelete(databasePath + "-wal");
        TryDelete(databasePath + "-shm");
    }

    private static void TryDelete(string path)
    {
        try
        {
            if (File.Exists(path))
                File.Delete(path);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
        }
    }

    private static string BuildConnectionString(string path, SqliteOpenMode mode) =>
        new SqliteConnectionStringBuilder
        {
            DataSource = path,
            Mode = mode,
            Pooling = false
        }.ToString();

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
