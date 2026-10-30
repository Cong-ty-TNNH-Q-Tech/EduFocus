using EduFocus.SqliteDataAccess.Persistence;
using EduFocus.SqliteDataAccess.Tests.TestSupport;
using FluentAssertions;

namespace EduFocus.SqliteDataAccess.Tests;

public sealed class BackupRestoreTests
{
    [Fact]
    public async Task Created_backup_is_verified_and_rotation_keeps_newest_three()
    {
        await using var db = new TempDatabase();
        await db.CreateInitializer().InitializeAsync();

        var store = db.CreateBackupStore();
        var backups = Enumerable.Range(0, 4).Select(_ => store.CreateBackup()).ToList();

        var remaining = Directory.GetFiles(db.BackupDirectory, "edufocus-*.db");

        remaining.Should().HaveCount(3);
        remaining.Should().Contain(backups[3]);
        remaining.Should().NotContain(backups[0]);
        remaining.Should().OnlyContain(path => store.VerifyIntegrity(path));
    }

    [Fact]
    public async Task Restore_replaces_database_and_keeps_pre_restore_snapshot()
    {
        await using var db = new TempDatabase();
        await db.CreateInitializer().InitializeAsync();

        var store = db.CreateBackupStore();
        var backupPath = store.CreateBackup();

        InsertSettingsRow(db);
        CountSettingsRows(db).Should().Be(1);

        var snapshotPath = db.CreateInitializer().RestoreBackup(backupPath);

        snapshotPath.Should().NotBeNull();
        File.Exists(snapshotPath).Should().BeTrue();
        store.VerifyIntegrity(snapshotPath!).Should().BeTrue();
        CountSettingsRows(db).Should().Be(0);
        store.VerifyIntegrity(db.DatabasePath).Should().BeTrue();
    }

    [Fact]
    public async Task Restore_rejects_missing_or_corrupt_backup_without_touching_database()
    {
        await using var db = new TempDatabase();
        await db.CreateInitializer().InitializeAsync();
        InsertSettingsRow(db);

        var store = db.CreateBackupStore();
        var corruptedBackup = Path.Combine(db.BackupDirectory, "edufocus-20260101-000000000-00.db");
        Directory.CreateDirectory(db.BackupDirectory);
        await File.WriteAllTextAsync(corruptedBackup, "not a sqlite database");

        var act = () => db.CreateInitializer().RestoreBackup(corruptedBackup);
        act.Should().Throw<SqliteBackupException>()
            .WithMessage("*integrity check*");

        var missingAct = () => db.CreateInitializer().RestoreBackup(
            Path.Combine(db.BackupDirectory, "missing.db"));
        missingAct.Should().Throw<SqliteBackupException>()
            .WithMessage("*not found*");

        CountSettingsRows(db).Should().Be(1);
        store.VerifyIntegrity(db.DatabasePath).Should().BeTrue();
    }

    [Fact]
    public async Task Corrupt_database_raises_safe_error_and_is_not_deleted()
    {
        await using var db = new TempDatabase();
        await db.CreateInitializer().InitializeAsync();

        Microsoft.Data.Sqlite.SqliteConnection.ClearAllPools();
        var corruptBytes = "this is not a sqlite database"u8.ToArray();
        await File.WriteAllBytesAsync(db.DatabasePath, corruptBytes);

        var act = async () => await db.CreateInitializer().InitializeAsync();

        var exception = (await act.Should().ThrowAsync<SqliteDatabaseCorruptException>()).Which;

        exception.BackupDirectory.Should().Be(db.BackupDirectory);
        exception.Message.Should().Contain("restore a backup");
        (await File.ReadAllBytesAsync(db.DatabasePath)).Should().Equal(corruptBytes);
        db.Logger.Errors.Should().Contain(message => message.Contains("integrity check failed"));
        db.Logger.Warnings.Should().Contain(message => message.Contains("Restore"));
    }

    [Fact]
    public async Task Unusable_data_directory_raises_safe_error_without_deleting_files()
    {
        await using var db = new TempDatabase();
        var blocker = Path.Combine(db.RootPath, "blocked");
        await File.WriteAllTextAsync(blocker, "keep me");

        var options = new EduFocusDatabaseOptions
        {
            DatabasePath = Path.Combine(blocker, "edufocus.db"),
            BackupDirectory = db.BackupDirectory,
            ProtectDataDirectory = false
        };

        var act = async () => await new SqliteDatabaseInitializer(options, db.Logger).InitializeAsync();

        var exception = (await act.Should().ThrowAsync<DataDirectorySecurityException>()).Which;

        exception.Message.Should().Contain("existing data was not modified");
        File.Exists(blocker).Should().BeTrue();
        (await File.ReadAllTextAsync(blocker)).Should().Be("keep me");
        db.Logger.Errors.Should().Contain(message => message.Contains("Could not prepare or restrict"));
    }

    private static void InsertSettingsRow(TempDatabase db)
    {
        using var connection = db.OpenConnection();
        TempDatabase.ExecuteNonQuery(
            connection,
            $"INSERT INTO AppSettings (Id, CreatedAtUtc, UpdatedAtUtc) VALUES ('{Guid.NewGuid()}', '2026-01-01T00:00:00.000Z', '2026-01-01T00:00:00.000Z');");
    }

    private static long CountSettingsRows(TempDatabase db)
    {
        using var connection = db.OpenConnection();
        return (long)TempDatabase.Scalar(connection, "SELECT COUNT(*) FROM AppSettings;")!;
    }
}
