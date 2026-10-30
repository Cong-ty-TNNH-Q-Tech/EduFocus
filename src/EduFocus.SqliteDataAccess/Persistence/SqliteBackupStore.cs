using System.Globalization;
using EduFocus.Ports.Outbound.Services;
using Microsoft.Data.Sqlite;

namespace EduFocus.SqliteDataAccess.Persistence;

/// <summary>
/// Creates, verifies, restores and rotates database backups. SQLite's backup
/// API does not support encrypted databases, so encrypted backups are produced
/// with <c>sqlcipher_export</c> into a new database keyed before use. Backup
/// files use unique timestamped names so a backup is never overwritten in
/// place, and at most <see cref="EduFocusDatabaseOptions.MaxBackups"/> files
/// are kept (database_schema §16, §17, §18).
/// </summary>
public sealed class SqliteBackupStore
{
    private const string BackupFilePattern = "edufocus-*.db";
    private const string TimestampFormat = "yyyyMMdd-HHmmssfff";

    private readonly EduFocusDatabaseOptions _options;
    private readonly IAppLogger _logger;
    private readonly IDatabaseKeyStore _keyStore;
    private readonly TimeProvider _timeProvider;

    public SqliteBackupStore(
        EduFocusDatabaseOptions options,
        IAppLogger logger,
        IDatabaseKeyStore keyStore,
        TimeProvider? timeProvider = null)
    {
        ArgumentNullException.ThrowIfNull(options);
        ArgumentNullException.ThrowIfNull(logger);
        ArgumentNullException.ThrowIfNull(keyStore);

        _options = options;
        _logger = logger;
        _keyStore = keyStore;
        _timeProvider = timeProvider ?? TimeProvider.System;
    }

    /// <summary>
    /// Creates and verifies a backup of the live database. Encrypted databases
    /// produce encrypted backups; a database that is still plaintext (only
    /// possible before the automatic upgrade) produces a plaintext backup that
    /// the initializer encrypts after a successful upgrade.
    /// </summary>
    public string CreateBackup()
    {
        var databasePath = Path.GetFullPath(_options.DatabasePath);
        var state = SqliteCipher.GetFileState(databasePath);

        if (state is SqliteDatabaseFileState.Missing or SqliteDatabaseFileState.Empty)
            throw new SqliteBackupException("There is no database file to back up.");

        var key = state == SqliteDatabaseFileState.Encrypted ? _keyStore.Get() : null;

        return CreateVerifiedBackup(databasePath, key);
    }

    /// <summary>Runs SQLite's integrity check without creating or modifying the file.</summary>
    public bool VerifyIntegrity(string path)
    {
        if (string.IsNullOrWhiteSpace(path) || !File.Exists(path))
            return false;

        var state = SqliteCipher.GetFileState(path);

        if (state is SqliteDatabaseFileState.Missing or SqliteDatabaseFileState.Empty)
            return false;

        var key = state == SqliteDatabaseFileState.Encrypted ? _keyStore.TryLoad() : null;

        if (state == SqliteDatabaseFileState.Encrypted && key is null)
            return false;

        return CheckIntegrity(Path.GetFullPath(path), key);
    }

    /// <summary>
    /// Replaces the database with a verified backup. Encrypted backups are
    /// copied; legacy plaintext backups are encrypted during the restore so the
    /// live database never becomes plaintext. The current database is
    /// snapshotted first on a best-effort basis, and is never touched when the
    /// backup is missing or corrupt. Stop EduFocus before calling this method;
    /// WAL/SHM side files are removed so old frames cannot leak into the
    /// restored database. Returns the pre-restore snapshot path, if one was
    /// created.
    /// </summary>
    public string? Restore(string backupPath)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(backupPath);

        var sourcePath = Path.GetFullPath(backupPath);
        var sourceState = SqliteCipher.GetFileState(sourcePath);

        if (sourceState == SqliteDatabaseFileState.Missing)
            throw new SqliteBackupException("Backup file was not found. Nothing was restored.");

        if (sourceState == SqliteDatabaseFileState.Empty)
            throw new SqliteBackupException(
                "Backup file failed the integrity check. Nothing was restored and the existing database was kept.");

        var key = sourceState == SqliteDatabaseFileState.Encrypted
            ? _keyStore.Get()
            : _keyStore.LoadOrCreate();

        var sourceKey = sourceState == SqliteDatabaseFileState.Encrypted ? key : null;

        if (!CheckIntegrity(sourcePath, sourceKey))
        {
            throw new SqliteBackupException(
                "Backup file failed the integrity check or was encrypted with a different key. Nothing was restored and the existing database was kept.");
        }

        var databasePath = Path.GetFullPath(_options.DatabasePath);
        var databaseDirectory = Path.GetDirectoryName(databasePath)
            ?? throw new SqliteBackupException("The database path is not valid. Nothing was restored.");

        Directory.CreateDirectory(databaseDirectory);
        SqliteConnection.ClearAllPools();

        var snapshotPath = File.Exists(databasePath)
            ? TrySnapshotCurrentDatabase(databasePath, key)
            : null;

        DeleteSideFiles(databasePath);

        var temporaryPath = Path.Combine(
            databaseDirectory,
            $".{Path.GetFileName(databasePath)}.{Guid.NewGuid():N}.restore");

        try
        {
            if (sourceKey is null)
                ExportPlaintextToEncrypted(sourcePath, temporaryPath, key);
            else
                File.Copy(sourcePath, temporaryPath, overwrite: true);

            if (!CheckIntegrity(temporaryPath, key))
            {
                TryDelete(temporaryPath);
                throw new SqliteBackupException(
                    "The copied backup failed the integrity check. Nothing was restored and the existing database was kept.");
            }

            File.Move(temporaryPath, databasePath, overwrite: true);
        }
        catch (Exception ex) when (IsFileAccessFailure(ex))
        {
            TryDelete(temporaryPath);
            throw new SqliteBackupException(
                "Could not restore the EduFocus database. The existing database was kept.",
                ex);
        }

        DeleteSideFiles(databasePath);
        EnableWalMode(databasePath, key);

        _logger.Info(LogCategories.Database, $"Database restored from '{sourcePath}'.");

        var backupDirectory = Path.GetFullPath(_options.ResolvedBackupDirectory);
        if (Directory.Exists(backupDirectory))
            ApplyRetention(backupDirectory, sourcePath, snapshotPath);

        return snapshotPath;
    }

    /// <summary>
    /// Encrypts backups written by EduFocus 1.x, which are plaintext. Failures
    /// keep the original file and are logged; the data directory ACL still
    /// protects it.
    /// </summary>
    public int EncryptLegacyBackups(DatabaseEncryptionKey key)
    {
        ArgumentNullException.ThrowIfNull(key);

        var backupDirectory = Path.GetFullPath(_options.ResolvedBackupDirectory);
        if (!Directory.Exists(backupDirectory))
            return 0;

        var encryptedCount = 0;

        foreach (var backupPath in Directory.EnumerateFiles(backupDirectory, BackupFilePattern))
        {
            if (SqliteCipher.GetFileState(backupPath) != SqliteDatabaseFileState.Plaintext)
                continue;

            var temporaryPath = $"{backupPath}.{Guid.NewGuid():N}.encrypting";

            try
            {
                ExportPlaintextToEncrypted(backupPath, temporaryPath, key);

                if (!CheckIntegrity(temporaryPath, key))
                    throw new SqliteBackupException("The encrypted backup failed the integrity check.");

                File.Move(temporaryPath, backupPath, overwrite: true);
                encryptedCount++;
            }
            catch (Exception ex) when (ex is SqliteException or IOException or UnauthorizedAccessException
                or SqliteBackupException)
            {
                TryDelete(temporaryPath);
                _logger.Warning(
                    LogCategories.Database,
                    $"Could not encrypt the legacy plaintext backup '{backupPath}'. The file was kept: {ex.Message}");
            }
        }

        if (encryptedCount > 0)
        {
            _logger.Info(
                LogCategories.Database,
                $"Encrypted {encryptedCount} legacy plaintext database backup(s) with the current key.");
        }

        return encryptedCount;
    }

    private string CreateVerifiedBackup(string databasePath, DatabaseEncryptionKey? key)
    {
        var backupDirectory = PrepareBackupDirectory();
        var backupPath = CreateUniqueBackupPath(backupDirectory);

        try
        {
            using var source = new SqliteConnection(
                BuildConnectionString(databasePath, SqliteOpenMode.ReadWriteCreate));

            source.Open();

            if (key is not null)
            {
                SqliteCipher.ApplyKey(source, key);
                SqliteCipher.ExportEncrypted(source, backupPath, key);
            }
            else
            {
                using var destination = new SqliteConnection(
                    BuildConnectionString(backupPath, SqliteOpenMode.ReadWriteCreate));
                destination.Open();
                source.BackupDatabase(destination);
            }
        }
        catch (Exception ex) when (IsFileAccessFailure(ex))
        {
            TryDelete(backupPath);
            throw new SqliteBackupException(
                "Could not back up the EduFocus database. Migration was not started and existing data was kept.",
                ex);
        }

        if (!CheckIntegrity(backupPath, key))
        {
            TryDelete(backupPath);
            throw new SqliteBackupException(
                "The EduFocus database backup failed the integrity check. Migration was not started and existing data was kept.");
        }

        _logger.Info(LogCategories.Database, $"Database backup created and verified at '{backupPath}'.");
        ApplyRetention(backupDirectory, backupPath);
        return backupPath;
    }

    /// <summary>
    /// Copies the current database to the backup folder. Failures are logged
    /// and do not block an explicit restore.
    /// </summary>
    private string? TrySnapshotCurrentDatabase(string databasePath, DatabaseEncryptionKey key)
    {
        try
        {
            var backupDirectory = PrepareBackupDirectory();
            var snapshotPath = CreateUniqueBackupPath(backupDirectory);

            var sourceState = SqliteCipher.GetFileState(databasePath);

            using var source = new SqliteConnection(
                BuildConnectionString(databasePath, SqliteOpenMode.ReadWriteCreate));
            source.Open();

            if (sourceState == SqliteDatabaseFileState.Encrypted)
            {
                SqliteCipher.ApplyKey(source, key);
                SqliteCipher.ExportEncrypted(source, snapshotPath, key);
            }
            else
            {
                using var destination = new SqliteConnection(
                    BuildConnectionString(snapshotPath, SqliteOpenMode.ReadWriteCreate));
                destination.Open();
                source.BackupDatabase(destination);
            }

            var snapshotKey = sourceState == SqliteDatabaseFileState.Encrypted ? key : null;

            if (!CheckIntegrity(snapshotPath, snapshotKey))
            {
                TryDelete(snapshotPath);
                _logger.Warning(
                    LogCategories.Database,
                    "The pre-restore snapshot failed the integrity check and was discarded; the restore continued.");
                return null;
            }

            _logger.Info(LogCategories.Database, $"Pre-restore database snapshot created at '{snapshotPath}'.");
            return snapshotPath;
        }
        catch (Exception ex) when (ex is SqliteBackupException || IsFileAccessFailure(ex))
        {
            _logger.Warning(LogCategories.Database, $"Could not snapshot the current database before restoring: {ex.Message}");
            return null;
        }
    }

    private static void ExportPlaintextToEncrypted(
        string sourcePath,
        string destinationPath,
        DatabaseEncryptionKey key)
    {
        using var source = new SqliteConnection(
            BuildConnectionString(sourcePath, SqliteOpenMode.ReadWriteCreate));

        source.Open();
        SqliteCipher.ExportEncrypted(source, destinationPath, key);
    }

    private void EnableWalMode(string databasePath, DatabaseEncryptionKey key)
    {
        using var connection = new SqliteConnection(
            BuildConnectionString(databasePath, SqliteOpenMode.ReadWrite));
        connection.Open();
        SqliteCipher.ApplyKey(connection, key);

        using var command = connection.CreateCommand();
        command.CommandText = "PRAGMA journal_mode = WAL;";
        command.ExecuteNonQuery();

        SqliteConnection.ClearPool(connection);
    }

    private bool CheckIntegrity(string path, DatabaseEncryptionKey? key)
    {
        if (!File.Exists(path))
            return false;

        try
        {
            using var connection = new SqliteConnection(
                BuildConnectionString(Path.GetFullPath(path), SqliteOpenMode.ReadWrite));

            connection.Open();

            if (key is not null)
                SqliteCipher.ApplyKey(connection, key);

            using var command = connection.CreateCommand();
            command.CommandText = "PRAGMA integrity_check;";

            return string.Equals(command.ExecuteScalar() as string, "ok", StringComparison.OrdinalIgnoreCase);
        }
        catch (Exception ex) when (IsFileAccessFailure(ex))
        {
            return false;
        }
    }

    private string PrepareBackupDirectory()
    {
        var backupDirectory = Path.GetFullPath(_options.ResolvedBackupDirectory);

        try
        {
            Directory.CreateDirectory(backupDirectory);
        }
        catch (Exception ex) when (IsFileAccessFailure(ex))
        {
            throw new SqliteBackupException(
                "Could not create the EduFocus backup folder. Migration was not started and existing data was kept.",
                ex);
        }

        if (!_options.ProtectDataDirectory)
            return backupDirectory;

        try
        {
            DataDirectorySecurity.Apply(backupDirectory);
        }
        catch (Exception ex) when (ex is UnauthorizedAccessException or IOException or DirectoryNotFoundException)
        {
            _logger.Error(LogCategories.Database, $"Could not protect the backup directory '{backupDirectory}'.", ex);
            throw new SqliteBackupException(
                "Could not protect the EduFocus backup folder. No data was deleted.",
                ex);
        }

        return backupDirectory;
    }

    private string CreateUniqueBackupPath(string backupDirectory)
    {
        var timestamp = _timeProvider
            .GetUtcNow()
            .UtcDateTime
            .ToString(TimestampFormat, CultureInfo.InvariantCulture);

        var path = string.Empty;
        var attempt = 0;

        do
        {
            path = Path.Combine(backupDirectory, $"edufocus-{timestamp}-{attempt:D2}.db");
            attempt++;
        }
        while (File.Exists(path));

        return path;
    }

    private void ApplyRetention(string backupDirectory, params string?[] protectedPaths)
    {
        var keep = Math.Max(1, _options.MaxBackups);
        var protectedSet = protectedPaths
            .Where(path => !string.IsNullOrEmpty(path))
            .Select(path => Path.GetFullPath(path!))
            .ToHashSet(StringComparer.OrdinalIgnoreCase);

        var backups = Directory
            .EnumerateFiles(backupDirectory, BackupFilePattern)
            .OrderByDescending(path => path, StringComparer.OrdinalIgnoreCase)
            .ToList();

        var remaining = backups.Count;

        foreach (var oldBackup in Enumerable.Reverse(backups))
        {
            if (remaining <= keep)
                break;

            if (protectedSet.Contains(Path.GetFullPath(oldBackup)))
                continue;

            try
            {
                File.Delete(oldBackup);
                remaining--;
                _logger.Info(LogCategories.Database, $"Removed old database backup '{oldBackup}'.");
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
            {
                _logger.Warning(LogCategories.Database, $"Could not remove old database backup '{oldBackup}': {ex.Message}");
            }
        }
    }

    private static void DeleteSideFiles(string databasePath)
    {
        DeleteIfExists(databasePath + "-wal");
        DeleteIfExists(databasePath + "-shm");
    }

    private static void DeleteIfExists(string path)
    {
        try
        {
            if (File.Exists(path))
                File.Delete(path);
        }
        catch (IOException ex)
        {
            throw new SqliteBackupException(
                "Could not remove SQLite side files before restoring. The existing database was kept.",
                ex);
        }
        catch (UnauthorizedAccessException ex)
        {
            throw new SqliteBackupException(
                "Could not remove SQLite side files before restoring. The existing database was kept.",
                ex);
        }
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

    private static bool IsFileAccessFailure(Exception ex) =>
        ex is SqliteException or IOException or UnauthorizedAccessException;

    private static string BuildConnectionString(string path, SqliteOpenMode mode) =>
        new SqliteConnectionStringBuilder
        {
            DataSource = path,
            Mode = mode,
            Pooling = false
        }.ToString();
}
