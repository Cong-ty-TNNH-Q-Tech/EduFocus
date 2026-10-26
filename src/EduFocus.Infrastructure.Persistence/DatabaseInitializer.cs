using EduFocus.Ports.Outbound.Services;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;

namespace EduFocus.Infrastructure.Persistence;

public sealed class DatabaseInitializer(IAppLogger logger)
{
    private readonly IAppLogger _logger = logger ?? throw new ArgumentNullException(nameof(logger));

    public static string GetDatabasePath(string? commonApplicationData = null)
    {
        var root = commonApplicationData ?? Environment.GetFolderPath(Environment.SpecialFolder.CommonApplicationData);
        if (string.IsNullOrWhiteSpace(root)) throw new InvalidOperationException("The common application data directory is unavailable.");
        return Path.Combine(root, "EduFocus", "edufocus.db");
    }

    public async Task InitializeAsync(string databasePath, CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(databasePath)) throw new ArgumentException("A database path is required.", nameof(databasePath));
        var fullPath = Path.GetFullPath(databasePath);
        if (!string.Equals(Path.GetFileName(fullPath), "edufocus.db", StringComparison.OrdinalIgnoreCase) ||
            !string.Equals(Path.GetFileName(Path.GetDirectoryName(fullPath)), "EduFocus", StringComparison.OrdinalIgnoreCase))
            throw new ArgumentException("Only the EduFocus 2.0 database path is accepted; legacy database paths are not migrated.", nameof(databasePath));

        var directory = Path.GetDirectoryName(fullPath)!;
        Directory.CreateDirectory(directory);
        var existedBeforeOpen = File.Exists(fullPath);
        var cs = new SqliteConnectionStringBuilder { DataSource = fullPath, Mode = SqliteOpenMode.ReadWriteCreate, ForeignKeys = true, Pooling = false }.ToString();
        try
        {
            await using (var connection = new SqliteConnection(cs))
            {
                await connection.OpenAsync(cancellationToken);
                await using var pragmas = connection.CreateCommand();
                pragmas.CommandText = "PRAGMA foreign_keys=ON; PRAGMA journal_mode=WAL;";
                await pragmas.ExecuteNonQueryAsync(cancellationToken);
                if (existedBeforeOpen)
                {
                    pragmas.CommandText = "SELECT COUNT(*) FROM sqlite_master WHERE type='table' AND name NOT LIKE 'sqlite_%' AND name <> '__EFMigrationsHistory';";
                    var hasApplicationTables = Convert.ToInt64(await pragmas.ExecuteScalarAsync(cancellationToken)) > 0;
                    pragmas.CommandText = "SELECT COUNT(*) FROM sqlite_master WHERE type='table' AND name='__EFMigrationsHistory';";
                    var hasMigrationHistory = Convert.ToInt64(await pragmas.ExecuteScalarAsync(cancellationToken)) > 0;
                    if (hasApplicationTables && !hasMigrationHistory)
                        throw new InvalidOperationException("The existing database has no EF migration history and was not modified by EduFocus. Legacy or unrecognized databases are never migrated.");
                }
            }

            var options = new DbContextOptionsBuilder<AppDbContext>().UseSqlite(cs, x => x.MigrationsAssembly(typeof(AppDbContext).Assembly.FullName)).Options;
            await using var db = new AppDbContext(options);
            var pending = (await db.Database.GetPendingMigrationsAsync(cancellationToken)).ToArray();
            var backupCreated = false;
            if (pending.Length > 0 && existedBeforeOpen && new FileInfo(fullPath).Length > 0)
            {
                var backupPath = await CreateBackupAsync(fullPath, cancellationToken);
                _logger.Info($"Database migration backup created: {Path.GetFileName(backupPath)}");
                backupCreated = true;
            }

            await ConfigureIncrementalVacuumAsync(fullPath, existedBeforeOpen, backupCreated, cancellationToken);
            if (pending.Length == 0) return;

            await db.Database.MigrateAsync(cancellationToken);
            _logger.Info($"EduFocus database schema ready; applied {pending.Length} migration(s).");
        }
        catch (Exception ex)
        {
            try { _logger.Error($"EduFocus database migration failed for '{fullPath}'. The database was retained. If it is an existing database, restore the newest pre-migration file from the EduFocus\\backups directory after reviewing the logs.", ex); }
            catch { /* Logging failure must not replace the migration error or trigger destructive recovery. */ }
            throw new InvalidOperationException($"Database initialization failed. The database was retained. Review the log and restore a pre-migration backup manually if needed. Database: {fullPath}", ex);
        }
    }

    private async Task ConfigureIncrementalVacuumAsync(string databasePath, bool existedBeforeOpen, bool backupCreated, CancellationToken cancellationToken)
    {
        var cs = new SqliteConnectionStringBuilder { DataSource = databasePath, Mode = SqliteOpenMode.ReadWrite, ForeignKeys = true, Pooling = false }.ToString();
        await using var connection = new SqliteConnection(cs);
        await connection.OpenAsync(cancellationToken);
        await using var read = connection.CreateCommand();
        read.CommandText = "PRAGMA auto_vacuum;";
        var autoVacuum = Convert.ToInt32(await read.ExecuteScalarAsync(cancellationToken));
        if (autoVacuum == 2) return;
        read.CommandText = "SELECT COUNT(*) FROM sqlite_master WHERE type='table' AND name NOT LIKE 'sqlite_%';";
        var hasTables = Convert.ToInt64(await read.ExecuteScalarAsync(cancellationToken)) > 0;
        if (hasTables && existedBeforeOpen && !backupCreated)
        {
            var backupPath = await CreateBackupAsync(databasePath, cancellationToken);
            _logger.Info($"Database backup created before enabling incremental vacuum: {Path.GetFileName(backupPath)}");
        }
        await using var configure = connection.CreateCommand();
        configure.CommandText = "PRAGMA auto_vacuum=INCREMENTAL; VACUUM;";
        await configure.ExecuteNonQueryAsync(cancellationToken);
    }

    private static async Task<string> CreateBackupAsync(string databasePath, CancellationToken cancellationToken)
    {
        var backupDirectory = Path.Combine(Path.GetDirectoryName(databasePath)!, "backups");
        Directory.CreateDirectory(backupDirectory);
        var backup = Path.Combine(backupDirectory, $"edufocus-{DateTime.UtcNow:yyyyMMddTHHmmssfffZ}.db");
        var sourceCs = new SqliteConnectionStringBuilder { DataSource = databasePath, Mode = SqliteOpenMode.ReadWrite, ForeignKeys = true, Pooling = false }.ToString();
        var targetCs = new SqliteConnectionStringBuilder { DataSource = backup, Mode = SqliteOpenMode.ReadWriteCreate, ForeignKeys = true, Pooling = false }.ToString();
        await using (var source = new SqliteConnection(sourceCs))
        await using (var target = new SqliteConnection(targetCs))
        {
            await source.OpenAsync(cancellationToken);
            await target.OpenAsync(cancellationToken);
            source.BackupDatabase(target);
        }
        foreach (var old in Directory.EnumerateFiles(backupDirectory, "edufocus-*.db").OrderByDescending(File.GetCreationTimeUtc).Skip(3)) File.Delete(old);
        return backup;
    }
}
