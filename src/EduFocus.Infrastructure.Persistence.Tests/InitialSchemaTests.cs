using EduFocus.Domain.Entities;
using EduFocus.Domain.Enums;
using EduFocus.Domain.ValueObjects;
using EduFocus.Infrastructure.Persistence;
using EduFocus.Ports.Outbound.Services;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;

namespace EduFocus.Infrastructure.Persistence.Tests;

public sealed class InitialSchemaTests
{
    private static readonly DateTimeOffset UtcNow = new(2026, 10, 26, 10, 0, 0, TimeSpan.Zero);

    [Fact]
    public async Task Initial_migration_creates_all_tables_persists_utc_and_credentials()
    {
        await using var connection = new SqliteConnection("Data Source=:memory:");
        await connection.OpenAsync();
        var options = new DbContextOptionsBuilder<AppDbContext>().UseSqlite(connection).Options;
        await using (var db = new AppDbContext(options))
        {
            await db.Database.MigrateAsync();
            var tables = await ReadTableNames(connection);
            Assert.Contains("AppSettings", tables); Assert.Contains("ParentCredentials", tables); Assert.Contains("BlockRules", tables);
            Assert.Contains("ScheduleWindows", tables); Assert.Contains("StudySessions", tables); Assert.Contains("ActivitySamples", tables);
            Assert.Contains("AttentionEvents", tables); Assert.Contains("BlockedAttempts", tables); Assert.Contains("AuditLogs", tables);

            db.AppSettings.Add(AppSettings.Create(UtcNow));
            db.ParentCredentials.Add(ParentCredentials.Create(Hash(1), Hash(2), UtcNow));
            var session = StudySession.Start(null, StudySessionTrigger.Manual, true, new DateOnly(2026, 10, 26), UtcNow);
            session.AddAttention(EduFocus.Domain.Attention.AttentionState.Focused, 60, .9, UtcNow.AddMinutes(1));
            db.StudySessions.Add(session);
            db.BlockRules.Add(BlockRule.Create(BlockRuleType.Domain, BlockMatchType.Exact, "chatgpt.com", BlockRuleCategory.Chatbot, BlockRuleEffect.Block, UtcNow));
            await db.SaveChangesAsync();

            var read = await db.StudySessions.AsNoTracking().SingleAsync();
            Assert.Equal(UtcNow, read.StartedAtUtc);
            Assert.Equal(TimeSpan.Zero, read.StartedAtUtc.Offset);
            Assert.Equal(60, read.MonitoredSeconds);
            Assert.Equal(100, read.AttentionPercent);
            var credentials = await db.ParentCredentials.AsNoTracking().SingleAsync();
            Assert.Equal(600_000, credentials.Pin.Iterations);
            Assert.Equal(32, Convert.FromBase64String(credentials.Pin.HashBase64).Length);
            db.ActivitySamples.Add(ActivitySample.Create(Guid.NewGuid(), UtcNow, 1, UtcNow));
            await Assert.ThrowsAsync<DbUpdateException>(() => db.SaveChangesAsync());
        }

        await using var pragma = connection.CreateCommand();
        pragma.CommandText = "PRAGMA foreign_keys;";
        Assert.Equal(1L, (long)(await pragma.ExecuteScalarAsync())!);
    }

    [Fact]
    public async Task Partial_unique_index_rejects_a_second_active_session()
    {
        await using var connection = new SqliteConnection("Data Source=:memory:");
        await connection.OpenAsync();
        var options = new DbContextOptionsBuilder<AppDbContext>().UseSqlite(connection).Options;
        await using var db = new AppDbContext(options);
        await db.Database.MigrateAsync();
        db.StudySessions.Add(StudySession.Start(null, StudySessionTrigger.Manual, false, new DateOnly(2026, 10, 26), UtcNow));
        db.StudySessions.Add(StudySession.Start(null, StudySessionTrigger.Recovery, false, new DateOnly(2026, 10, 26), UtcNow.AddMinutes(1)));
        await Assert.ThrowsAsync<DbUpdateException>(() => db.SaveChangesAsync());
    }

    [Fact]
    public async Task Initializer_creates_a_new_database_with_required_sqlite_pragmas()
    {
        var root = Path.Combine(Path.GetTempPath(), "EduFocus-Persistence-" + Guid.NewGuid().ToString("N"));
        var directory = Path.Combine(root, "EduFocus");
        Directory.CreateDirectory(directory);
        var path = Path.Combine(directory, "edufocus.db");
        try
        {
            await new DatabaseInitializer(new TestLogger(), new TestProtector()).InitializeAsync(path);
            Assert.True(File.Exists(path));
            await using var connection = new SqliteConnection(new SqliteConnectionStringBuilder { DataSource = path, ForeignKeys = true, Pooling = false }.ToString());
            await connection.OpenAsync();
            var tables = await ReadTableNames(connection);
            Assert.Contains("AppSettings", tables); Assert.Contains("ParentCredentials", tables); Assert.Contains("BlockRules", tables);
            Assert.Contains("ScheduleWindows", tables); Assert.Contains("StudySessions", tables); Assert.Contains("ActivitySamples", tables);
            Assert.Contains("AttentionEvents", tables); Assert.Contains("BlockedAttempts", tables); Assert.Contains("AuditLogs", tables);
            Assert.Equal("wal", await ScalarText(connection, "PRAGMA journal_mode;"));
            Assert.Equal(2L, (long)(await Scalar(connection, "PRAGMA auto_vacuum;"))!);
            Assert.Equal(1L, (long)(await Scalar(connection, "PRAGMA foreign_keys;"))!);
            await connection.CloseAsync();
        }
        finally
        {
            var fullRoot = Path.GetFullPath(root);
            var tempRoot = Path.GetFullPath(Path.GetTempPath());
            if (fullRoot.StartsWith(tempRoot, StringComparison.OrdinalIgnoreCase) && Directory.Exists(fullRoot)) Directory.Delete(fullRoot, true);
        }
    }

    [Fact]
    public async Task Existing_database_is_backed_up_before_failed_migration_and_is_retained()
    {
        var root = Path.Combine(Path.GetTempPath(), "EduFocus-Persistence-" + Guid.NewGuid().ToString("N"));
        var directory = Path.Combine(root, "EduFocus"); Directory.CreateDirectory(directory);
        var path = Path.Combine(directory, "edufocus.db");
        try
        {
            await using (var seedConnection = new SqliteConnection(new SqliteConnectionStringBuilder { DataSource = path, Pooling = false }.ToString()))
            {
                await seedConnection.OpenAsync();
                var options = new DbContextOptionsBuilder<AppDbContext>().UseSqlite(seedConnection).Options;
                await using var seed = new AppDbContext(options);
                await seed.Database.MigrateAsync();
                seed.AppSettings.Add(AppSettings.Create(UtcNow));
                await seed.SaveChangesAsync();
                await seedConnection.CloseAsync();
            }
            await using (var seed = new SqliteConnection(new SqliteConnectionStringBuilder { DataSource = path, Pooling = false }.ToString()))
            {
                await seed.OpenAsync();
                await using var command = seed.CreateCommand();
                command.CommandText = "DELETE FROM __EFMigrationsHistory; INSERT INTO __EFMigrationsHistory(MigrationId,ProductVersion) VALUES ('LegacyBaseline','10.0.13');";
                await command.ExecuteNonQueryAsync();
            }

            var logger = new TestLogger();
            for (var i = 0; i < 4; i++)
                await Assert.ThrowsAsync<InvalidOperationException>(() => new DatabaseInitializer(logger, new TestProtector()).InitializeAsync(path));
            Assert.NotEmpty(logger.Errors);
            Assert.Contains("retained", logger.Errors[^1], StringComparison.OrdinalIgnoreCase);
            var backupPaths = Directory.EnumerateFiles(Path.Combine(directory, "backups"), "edufocus-*.db").ToArray();
            Assert.Equal(3, backupPaths.Length);
            Assert.Equal(3, backupPaths.Select(Path.GetFileName).Distinct(StringComparer.OrdinalIgnoreCase).Count());
            Assert.True(File.Exists(path));
            await using var current = new SqliteConnection(new SqliteConnectionStringBuilder { DataSource = path, Pooling = false }.ToString());
            await current.OpenAsync();
            Assert.Equal(1L, (long)(await Scalar(current, "SELECT COUNT(*) FROM AppSettings"))!);
            Assert.All(backupPaths, backupPath => Assert.True(new FileInfo(backupPath).Length > 0));
            await current.CloseAsync();
        }
        finally
        {
            var fullRoot = Path.GetFullPath(root);
            var tempRoot = Path.GetFullPath(Path.GetTempPath());
            if (fullRoot.StartsWith(tempRoot, StringComparison.OrdinalIgnoreCase) && Directory.Exists(fullRoot)) Directory.Delete(fullRoot, true);
        }
    }

    [Fact]
    public async Task Initializer_rejects_legacy_database_location_without_moving_or_deleting_it()
    {
        var root = Path.Combine(Path.GetTempPath(), "DontBeLazy-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);
        var oldPath = Path.Combine(root, "DontBeLazy.db");
        await File.WriteAllTextAsync(oldPath, "legacy-canary");
        try
        {
            await Assert.ThrowsAsync<ArgumentException>(() => new DatabaseInitializer(new TestLogger(), new TestProtector()).InitializeAsync(oldPath));
            Assert.Equal("legacy-canary", await File.ReadAllTextAsync(oldPath));
        }
        finally
        {
            if (Path.GetFullPath(root).StartsWith(Path.GetFullPath(Path.GetTempPath()), StringComparison.OrdinalIgnoreCase)) Directory.Delete(root, true);
        }
    }

    [Fact]
    public async Task Initializer_refuses_untracked_legacy_tables_at_the_new_database_path()
    {
        var root = Path.Combine(Path.GetTempPath(), "EduFocus-Legacy-" + Guid.NewGuid().ToString("N"));
        var directory = Path.Combine(root, "EduFocus"); Directory.CreateDirectory(directory);
        var path = Path.Combine(directory, "edufocus.db");
        try
        {
            await using (var connection = new SqliteConnection(new SqliteConnectionStringBuilder { DataSource = path, Pooling = false }.ToString()))
            {
                await connection.OpenAsync();
                await using var command = connection.CreateCommand();
                command.CommandText = "CREATE TABLE OldTask (Id INTEGER PRIMARY KEY, Name TEXT NOT NULL); INSERT INTO OldTask(Name) VALUES ('legacy-data');";
                await command.ExecuteNonQueryAsync();
            }
            await Assert.ThrowsAsync<InvalidOperationException>(() => new DatabaseInitializer(new TestLogger(), new TestProtector()).InitializeAsync(path));
            await using var verify = new SqliteConnection(new SqliteConnectionStringBuilder { DataSource = path, Pooling = false }.ToString());
            await verify.OpenAsync();
            Assert.Equal("legacy-data", await ScalarText(verify, "SELECT Name FROM OldTask"));
            Assert.DoesNotContain("AppSettings", await ReadTableNames(verify));
            await verify.CloseAsync();
        }
        finally
        {
            var fullRoot = Path.GetFullPath(root);
            if (fullRoot.StartsWith(Path.GetFullPath(Path.GetTempPath()), StringComparison.OrdinalIgnoreCase) && Directory.Exists(fullRoot)) Directory.Delete(fullRoot, true);
        }
    }

    [Fact]
    public async Task Recovery_verifies_backup_then_restores_it_while_quarantining_current_database()
    {
        var root = Path.Combine(Path.GetTempPath(), "EduFocus-Restore-" + Guid.NewGuid().ToString("N"));
        var directory = Path.Combine(root, "EduFocus"); var backups = Path.Combine(directory, "backups");
        Directory.CreateDirectory(backups);
        var target = Path.Combine(directory, "edufocus.db");
        var backup = Path.Combine(backups, $"edufocus-{DateTime.UtcNow:yyyyMMddTHHmmssfffZ}-{Guid.NewGuid():N}.db");
        try
        {
            await CreateDatabaseWithAuditAsync(target, "current database");
            await CreateDatabaseWithAuditAsync(backup, "backup snapshot");
            var recovery = new DatabaseRecoveryService(new TestLogger(), new TestProtector());
            Assert.True(await recovery.VerifyAsync(backup));
            var corrupt = Path.Combine(backups, $"edufocus-corrupt-{Guid.NewGuid():N}.db");
            await File.WriteAllTextAsync(corrupt, "not a SQLite database");
            Assert.False(await recovery.VerifyAsync(corrupt));
            Assert.True(File.Exists(corrupt));
            await recovery.RestoreAsync(target, backup);
            await using var connection = new SqliteConnection(new SqliteConnectionStringBuilder { DataSource = target, Pooling = false }.ToString());
            await connection.OpenAsync();
            Assert.Equal("backup snapshot", await ScalarText(connection, "SELECT Description FROM AuditLogs LIMIT 1"));
            Assert.Contains(Directory.EnumerateDirectories(directory), path => Path.GetFileName(path).StartsWith("recovery-", StringComparison.Ordinal));
            await connection.CloseAsync();
        }
        finally
        {
            var fullRoot = Path.GetFullPath(root);
            if (fullRoot.StartsWith(Path.GetFullPath(Path.GetTempPath()), StringComparison.OrdinalIgnoreCase) && Directory.Exists(fullRoot)) Directory.Delete(fullRoot, true);
        }
    }

    private static CredentialHash Hash(byte seed) => CredentialHash.FromPbkdf2(Convert.ToBase64String(Enumerable.Repeat(seed, 32).ToArray()), Convert.ToBase64String(Enumerable.Repeat((byte)(seed + 10), 16).ToArray()), 600_000);
    private static async Task CreateDatabaseWithAuditAsync(string path, string description)
    {
        await using var connection = new SqliteConnection(new SqliteConnectionStringBuilder { DataSource = path, Pooling = false }.ToString());
        await connection.OpenAsync();
        var options = new DbContextOptionsBuilder<AppDbContext>().UseSqlite(connection).Options;
        await using var db = new AppDbContext(options);
        await db.Database.MigrateAsync();
        db.AuditLogs.Add(AuditLog.Create(UtcNow, AuditActor.System, "RecoveryTest", description, UtcNow));
        await db.SaveChangesAsync();
        await connection.CloseAsync();
    }
    private static async Task<HashSet<string>> ReadTableNames(SqliteConnection connection)
    {
        var names = new HashSet<string>(StringComparer.Ordinal);
        await using var command = connection.CreateCommand(); command.CommandText = "SELECT name FROM sqlite_master WHERE type='table';";
        await using var reader = await command.ExecuteReaderAsync(); while (await reader.ReadAsync()) names.Add(reader.GetString(0)); return names;
    }
    private static async Task<object?> Scalar(SqliteConnection connection,string sql){await using var c=connection.CreateCommand();c.CommandText=sql;return await c.ExecuteScalarAsync();}
    private static async Task<string?> ScalarText(SqliteConnection connection,string sql)=>Convert.ToString(await Scalar(connection,sql));
    private sealed class TestLogger : IAppLogger
    {
        public List<string> Errors { get; } = [];
        public void Info(string message) { }
        public void Warning(string message) { }
        public void Error(string message, Exception? ex = null) => Errors.Add(message);
    }
    private sealed class TestProtector : IDataDirectoryProtector { public void Protect(string directoryPath) { } }
}
