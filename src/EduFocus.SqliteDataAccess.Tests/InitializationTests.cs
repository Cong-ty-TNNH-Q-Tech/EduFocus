using EduFocus.SqliteDataAccess.Persistence;
using EduFocus.SqliteDataAccess.Tests.TestSupport;
using FluentAssertions;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;

namespace EduFocus.SqliteDataAccess.Tests;

public sealed class InitializationTests
{
    [Fact]
    public async Task First_run_creates_schema_without_backup()
    {
        await using var db = new TempDatabase();

        var result = await db.CreateInitializer().InitializeAsync();

        result.WasCreated.Should().BeTrue();
        result.AppliedMigrations.Should().HaveCount(4)
            .And.Contain(migration => migration.EndsWith("_InitialEduFocusSchema"))
            .And.Contain(migration => migration.EndsWith("_AddOnboardingConsent"))
            .And.Contain(migration => migration.EndsWith("_AddProtectionSource"))
            .And.Contain(migration => migration.EndsWith("_AddUpdateCheckOptIn"));
        result.BackupPath.Should().BeNull();
        File.Exists(db.DatabasePath).Should().BeTrue();
        db.Logger.Errors.Should().BeEmpty();
    }

    [Fact]
    public async Task Second_run_applies_nothing_and_creates_no_backup()
    {
        await using var db = new TempDatabase();
        await db.CreateInitializer().InitializeAsync();

        var result = await db.CreateInitializer().InitializeAsync();

        result.WasCreated.Should().BeFalse();
        result.AppliedMigrations.Should().BeEmpty();
        result.BackupPath.Should().BeNull();
        Directory.Exists(db.BackupDirectory).Should().BeFalse();
        db.Logger.Infos.Should().Contain(message => message.Contains("up to date"));
    }

    [Fact]
    public async Task Connections_enable_foreign_keys_wal_and_incremental_auto_vacuum()
    {
        await using var db = new TempDatabase();
        await db.CreateInitializer().InitializeAsync();

        await using var context = db.CreateContext();
        await context.Database.OpenConnectionAsync();
        var connection = (SqliteConnection)context.Database.GetDbConnection();

        Convert.ToInt64(TempDatabase.Scalar(connection, "PRAGMA foreign_keys;")).Should().Be(1);
        TempDatabase.Scalar(connection, "PRAGMA journal_mode;").Should().Be("wal");
        Convert.ToInt64(TempDatabase.Scalar(connection, "PRAGMA auto_vacuum;")).Should().Be(2);

        await context.Database.CloseConnectionAsync();
    }

    [Fact]
    public async Task Failed_migration_keeps_database_and_writes_backup()
    {
        await using var db = new TempDatabase();
        CreateConflictingDatabase(db.DatabasePath);

        var act = async () => await db.CreateInitializer().InitializeAsync();

        var exception = (await act.Should().ThrowAsync<SqliteMigrationException>()).Which;

        exception.BackupPath.Should().NotBeNull();
        File.Exists(db.DatabasePath).Should().BeTrue();
        File.Exists(exception.BackupPath).Should().BeTrue();
        db.Logger.Errors.Should().ContainSingle()
            .Which.Should().Contain("was kept");
        db.Logger.Warnings.Should().Contain(message => message.Contains("Restore"));

        using var backup = OpenReadOnly(exception.BackupPath!);
        TempDatabase.Scalar(
                backup,
                "SELECT COUNT(*) FROM sqlite_master WHERE type = 'table' AND name = 'AppSettings';")
            .Should().Be(1L);
    }

    [Fact]
    public async Task Backup_retention_keeps_newest_files()
    {
        await using var db = new TempDatabase();
        Directory.CreateDirectory(db.BackupDirectory);

        var oldBackups = new[]
        {
            Path.Combine(db.BackupDirectory, "edufocus-20200101-000000000.db"),
            Path.Combine(db.BackupDirectory, "edufocus-20200102-000000000.db"),
            Path.Combine(db.BackupDirectory, "edufocus-20200103-000000000.db")
        };

        foreach (var oldBackup in oldBackups)
            await File.WriteAllTextAsync(oldBackup, "old backup placeholder");

        var options = new EduFocusDatabaseOptions
        {
            DatabasePath = db.DatabasePath,
            BackupDirectory = db.BackupDirectory,
            MaxBackups = 2,
            ProtectDataDirectory = false
        };

        CreateConflictingDatabase(db.DatabasePath);

        await Assert.ThrowsAsync<SqliteMigrationException>(
            () => new SqliteDatabaseInitializer(options, db.Logger).InitializeAsync());

        var remaining = Directory.GetFiles(db.BackupDirectory, "edufocus-*.db");

        remaining.Should().HaveCount(2);
        remaining.Should().Contain(oldBackups[2]);
        remaining.Should().NotContain(oldBackups[0]);
        remaining.Should().NotContain(oldBackups[1]);
    }

    [Fact]
    public async Task Upgrade_applies_pending_migrations_and_preserves_existing_settings()
    {
        await using var db = new TempDatabase();

        await using (var context = db.CreateContext())
        {
            await context.GetService<IMigrator>()
                .MigrateAsync("20261029140816_InitialEduFocusSchema");
        }

        using (var connection = db.OpenConnectionWithPragmas())
        {
            TempDatabase.ExecuteNonQuery(connection, """
                INSERT INTO AppSettings
                    (Id, ProtectionEnabled, MonitoringEnabled, ScheduleEnabled, MonitorOnlyDuringClasses,
                     DistractionThresholdSeconds, AbsenceThresholdSeconds, SampleIntervalSeconds,
                     ActivityRetentionDays, EventRetentionDays, SessionRetentionDays, StartWithWindows,
                     Theme, Language, OnboardingCompleted, CreatedAtUtc, UpdatedAtUtc)
                VALUES
                    ('11111111-1111-1111-1111-111111111111', 1, 1, 0, 0, 20, 30, 15, 30, 90, 365, 1,
                     'dark', 'vi', 0, '2026-10-29T00:00:00.000Z', '2026-10-29T00:00:00.000Z');
                """);
        }

        var result = await db.CreateInitializer().InitializeAsync();

        result.AppliedMigrations.Should().HaveCount(3);
        result.AppliedMigrations.Last().Should().EndWith("_AddUpdateCheckOptIn");
        result.BackupPath.Should().NotBeNullOrEmpty();
        File.Exists(result.BackupPath!).Should().BeTrue();

        using var upgraded = db.OpenConnection();
        TempDatabase.Scalar(upgraded, "SELECT ProtectionEnabled FROM AppSettings;").Should().Be(1L);
        TempDatabase.Scalar(upgraded, "SELECT ProtectionSource FROM AppSettings;").Should().Be("Manual");
        TempDatabase.Scalar(upgraded, "SELECT PrivacyAcceptedAtUtc FROM AppSettings;").Should().Be(DBNull.Value);
        TempDatabase.Scalar(upgraded, "SELECT GuardianAcknowledgedAtUtc FROM AppSettings;").Should().Be(DBNull.Value);
        TempDatabase.Scalar(upgraded, "SELECT UpdateCheckEnabled FROM AppSettings;").Should().Be(0L);
    }

    [Fact]
    public void Default_path_uses_program_data_and_not_the_legacy_app_data_database()
    {
        var expectedDirectory = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.CommonApplicationData),
            "EduFocus");

        EduFocusDatabaseOptions.DefaultDirectory.Should().Be(expectedDirectory);
        EduFocusDatabaseOptions.DefaultDatabasePath
            .Should().Be(Path.Combine(expectedDirectory, "edufocus.db"));

        var options = new EduFocusDatabaseOptions();

        options.DatabasePath.Should().Be(EduFocusDatabaseOptions.DefaultDatabasePath);
        options.ResolvedBackupDirectory.Should().Be(Path.Combine(expectedDirectory, "backups"));
        options.DatabasePath.Should().NotContain("DontBeLazy");
        options.DatabasePath.Should().NotContain(Path.Combine("AppData", "Roaming"));
        options.DatabasePath.Should().NotContain(Path.Combine("AppData", "Local"));
    }

    private static void CreateConflictingDatabase(string databasePath)
    {
        using var connection = new SqliteConnection(
            new SqliteConnectionStringBuilder { DataSource = databasePath }.ToString());
        connection.Open();
        TempDatabase.ExecuteNonQuery(connection, "CREATE TABLE AppSettings (Id TEXT NOT NULL PRIMARY KEY);");
    }

    private static SqliteConnection OpenReadOnly(string path)
    {
        var connection = new SqliteConnection(
            new SqliteConnectionStringBuilder
            {
                DataSource = path,
                Mode = SqliteOpenMode.ReadOnly
            }.ToString());
        connection.Open();
        return connection;
    }
}
