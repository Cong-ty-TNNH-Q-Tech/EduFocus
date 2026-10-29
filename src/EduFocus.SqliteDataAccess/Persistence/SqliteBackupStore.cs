using System.Globalization;
using EduFocus.Ports.Outbound.Services;
using Microsoft.Data.Sqlite;

namespace EduFocus.SqliteDataAccess.Persistence;

/// <summary>
/// Creates, verifies, restores and rotates database backups. Backup files use
/// unique timestamped names so a backup is never overwritten in place, and at
/// most <see cref="EduFocusDatabaseOptions.MaxBackups"/> files are kept
/// (database_schema §16, §17).
/// </summary>
public sealed class SqliteBackupStore
{
    private const string BackupFilePattern = "edufocus-*.db";
    private const string TimestampFormat = "yyyyMMdd-HHmmssfff";

    private readonly EduFocusDatabaseOptions _options;
    private readonly IAppLogger _logger;
    private readonly TimeProvider _timeProvider;

    public SqliteBackupStore(
        EduFocusDatabaseOptions options,
        IAppLogger logger,
        TimeProvider? timeProvider = null)
    {
        ArgumentNullException.ThrowIfNull(options);
        ArgumentNullException.ThrowIfNull(logger);

        _options = options;
        _logger = logger;
        _timeProvider = timeProvider ?? TimeProvider.System;
    }

    /// <summary>
    /// Copies the live database with the SQLite backup API and verifies the
    /// copy. Throws <see cref="SqliteBackupException"/> before migration when
    /// the copy cannot be created or fails the integrity check.
    /// </summary>
    public string CreateBackup()
    {
        var databasePath = Path.GetFullPath(_options.DatabasePath);
        if (!File.Exists(databasePath))
            throw new SqliteBackupException("There is no database file to back up.");

        var backupDirectory = PrepareBackupDirectory();
        var backupPath = CreateUniqueBackupPath(backupDirectory);

        try
        {
            using var source = new SqliteConnection(BuildConnectionString(databasePath, SqliteOpenMode.ReadWrite));
            using var destination = new SqliteConnection(
                BuildConnectionString(backupPath, SqliteOpenMode.ReadWriteCreate));

            source.Open();
            destination.Open();
            source.BackupDatabase(destination);
        }
        catch (Exception ex) when (IsFileAccessFailure(ex))
        {
            TryDelete(backupPath);
            throw new SqliteBackupException(
                "Could not back up the EduFocus database. Migration was not started and existing data was kept.",
                ex);
        }

        if (!VerifyIntegrity(backupPath))
        {
            TryDelete(backupPath);
            throw new SqliteBackupException(
                "The EduFocus database backup failed the integrity check. Migration was not started and existing data was kept.");
        }

        _logger.Info(LogCategories.Database, $"Database backup created and verified at '{backupPath}'.");
        ApplyRetention(backupDirectory, backupPath);
        return backupPath;
    }

    /// <summary>Runs SQLite's integrity check without creating the file.</summary>
    public bool VerifyIntegrity(string path)
    {
        if (string.IsNullOrWhiteSpace(path) || !File.Exists(path))
            return false;

        try
        {
            using var connection = new SqliteConnection(
                BuildConnectionString(Path.GetFullPath(path), SqliteOpenMode.ReadWrite));

            connection.Open();
            using var command = connection.CreateCommand();
            command.CommandText = "PRAGMA integrity_check;";

            return string.Equals(command.ExecuteScalar() as string, "ok", StringComparison.OrdinalIgnoreCase);
        }
        catch (Exception ex) when (IsFileAccessFailure(ex))
        {
            return false;
        }
    }

    /// <summary>
    /// Replaces the database with a verified backup. The current database is
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
        if (!File.Exists(sourcePath))
            throw new SqliteBackupException("Backup file was not found. Nothing was restored.");

        if (!VerifyIntegrity(sourcePath))
        {
            throw new SqliteBackupException(
                "Backup file failed the integrity check. Nothing was restored and the existing database was kept.");
        }

        var databasePath = Path.GetFullPath(_options.DatabasePath);
        var databaseDirectory = Path.GetDirectoryName(databasePath)
            ?? throw new SqliteBackupException("The database path is not valid. Nothing was restored.");

        Directory.CreateDirectory(databaseDirectory);
        SqliteConnection.ClearAllPools();

        var snapshotPath = File.Exists(databasePath)
            ? TrySnapshotCurrentDatabase(databasePath)
            : null;

        DeleteSideFiles(databasePath);

        var temporaryPath = Path.Combine(
            databaseDirectory,
            $".{Path.GetFileName(databasePath)}.{Guid.NewGuid():N}.restore");

        try
        {
            File.Copy(sourcePath, temporaryPath, overwrite: true);

            if (!VerifyIntegrity(temporaryPath))
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

        _logger.Info(LogCategories.Database, $"Database restored from '{sourcePath}'.");

        var backupDirectory = Path.GetFullPath(_options.ResolvedBackupDirectory);
        if (Directory.Exists(backupDirectory))
            ApplyRetention(backupDirectory, sourcePath, snapshotPath);

        return snapshotPath;
    }

    /// <summary>
    /// Copies the current database to the backup folder. Failures are logged
    /// and do not block an explicit restore.
    /// </summary>
    private string? TrySnapshotCurrentDatabase(string databasePath)
    {
        try
        {
            var backupDirectory = PrepareBackupDirectory();
            var snapshotPath = CreateUniqueBackupPath(backupDirectory);

            using var source = new SqliteConnection(BuildConnectionString(databasePath, SqliteOpenMode.ReadWrite));
            using var destination = new SqliteConnection(
                BuildConnectionString(snapshotPath, SqliteOpenMode.ReadWriteCreate));

            source.Open();
            destination.Open();
            source.BackupDatabase(destination);

            if (!VerifyIntegrity(snapshotPath))
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
