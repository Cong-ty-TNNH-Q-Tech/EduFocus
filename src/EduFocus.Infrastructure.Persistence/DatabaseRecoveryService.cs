using EduFocus.Ports.Outbound.Services;
using Microsoft.Data.Sqlite;

namespace EduFocus.Infrastructure.Persistence;

public sealed class DatabaseRecoveryService(IAppLogger logger, IDataDirectoryProtector directoryProtector)
{
    private readonly IAppLogger _logger = logger ?? throw new ArgumentNullException(nameof(logger));
    private readonly IDataDirectoryProtector _directoryProtector = directoryProtector ?? throw new ArgumentNullException(nameof(directoryProtector));

    public async Task<bool> VerifyAsync(string databasePath, CancellationToken cancellationToken = default)
    {
        try { await SqliteIntegrity.VerifyAsync(databasePath, cancellationToken); return true; }
        catch (Exception ex) when (ex is SqliteException or InvalidDataException or IOException or UnauthorizedAccessException or System.Security.SecurityException)
        {
            _logger.Error("SQLite integrity check failed. The database was not changed.", ex);
            return false;
        }
    }

    public async Task RestoreAsync(string databasePath, string backupPath, CancellationToken cancellationToken = default)
    {
        DatabaseInitializer.ValidateDatabasePath(databasePath);
        var target = Path.GetFullPath(databasePath);
        var directory = Path.GetDirectoryName(target)!;
        var backups = Path.Combine(directory, "backups");
        var source = Path.GetFullPath(backupPath);
        var relative = Path.GetRelativePath(backups, source);
        if (relative.StartsWith("..", StringComparison.Ordinal) || Path.IsPathRooted(relative) ||
            !Path.GetFileName(source).StartsWith("edufocus-", StringComparison.OrdinalIgnoreCase) ||
            !string.Equals(Path.GetExtension(source), ".db", StringComparison.OrdinalIgnoreCase) || !File.Exists(source))
            throw new ArgumentException("Restore source must be an existing EduFocus backup file.", nameof(backupPath));

        await SqliteIntegrity.VerifyAsync(source, cancellationToken);
        Directory.CreateDirectory(directory);
        var staging = Path.Combine(directory, $".restore-{Guid.NewGuid():N}.db");
        try
        {
            _directoryProtector.Protect(directory);
            File.Copy(source, staging, overwrite: false);
            await SqliteIntegrity.VerifyAsync(staging, cancellationToken);

            var quarantine = Path.Combine(directory, $"recovery-{DateTime.UtcNow:yyyyMMddTHHmmssfffZ}-{Guid.NewGuid():N}");
            Directory.CreateDirectory(quarantine);
            _directoryProtector.Protect(directory);
            MoveIfExists(target, Path.Combine(quarantine, "edufocus.db"));
            MoveIfExists(target + "-wal", Path.Combine(quarantine, "edufocus.db-wal"));
            MoveIfExists(target + "-shm", Path.Combine(quarantine, "edufocus.db-shm"));
            File.Move(staging, target);
            _directoryProtector.Protect(directory);
            _logger.Info($"EduFocus database restored from '{Path.GetFileName(source)}'; prior files retained in '{Path.GetFileName(quarantine)}'.");
        }
        catch (Exception ex)
        {
            try { _logger.Error("Database restore failed. Existing database and recovery files were retained where possible; no files were automatically deleted.", ex); } catch { }
            throw new InvalidOperationException("Database restore failed. Existing files were retained where possible. Review the log before attempting recovery again.", ex);
        }
    }

    private static void MoveIfExists(string source, string destination) { if (File.Exists(source)) File.Move(source, destination); }
}
