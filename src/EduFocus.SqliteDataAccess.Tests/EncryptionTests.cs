using System.Security.Cryptography;
using System.Text;
using EduFocus.SqliteDataAccess.Persistence;
using EduFocus.SqliteDataAccess.Tests.TestSupport;
using FluentAssertions;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;

namespace EduFocus.SqliteDataAccess.Tests;

public sealed class EncryptionTests
{
    private const string Marker = "WAL-SECRET-MARKER-42";

    [Fact]
    public async Task Database_file_is_encrypted_and_unreadable_without_the_key()
    {
        await using var db = new TempDatabase();

        await db.CreateInitializer().InitializeAsync();

        SqliteCipher.GetFileState(db.DatabasePath).Should().Be(SqliteDatabaseFileState.Encrypted);
        File.Exists(db.KeyStore.KeyFilePath).Should().BeTrue();

        var header = ReadHeader(db.DatabasePath);
        header.Should().NotEqual("SQLite format 3\0"u8.ToArray());

        using var connection = OpenWithoutKey(db.DatabasePath);
        var act = () => TempDatabase.Scalar(connection, "SELECT COUNT(*) FROM sqlite_master;");

        act.Should().Throw<SqliteException>().Which.SqliteErrorCode.Should().Be(26);
    }

    [Fact]
    public async Task Wrong_key_is_rejected_and_the_database_and_key_file_are_kept()
    {
        await using var db = new TempDatabase();
        await db.CreateInitializer().InitializeAsync();
        InsertSetting(db, Marker);

        var originalKeyFile = await File.ReadAllBytesAsync(db.KeyStore.KeyFilePath);

        await using var other = new TempDatabase();
        other.KeyStore.LoadOrCreate();
        File.Copy(other.KeyStore.KeyFilePath, db.KeyStore.KeyFilePath, overwrite: true);
        SqliteConnection.ClearAllPools();

        var wrongKey = new DatabaseKeyStore(db.Options).Get();
        using (var connection = OpenWithoutKey(db.DatabasePath))
        {
            wrongKey.ApplyTo(connection);
            var read = () => TempDatabase.Scalar(connection, "SELECT COUNT(*) FROM sqlite_master;");
            read.Should().Throw<SqliteException>().Which.SqliteErrorCode.Should().Be(26);
        }

        var act = () => new SqliteDatabaseInitializer(db.Options, db.Logger, new DatabaseKeyStore(db.Options))
            .InitializeAsync();

        await act.Should().ThrowAsync<SqliteDatabaseCorruptException>();
        (await File.ReadAllBytesAsync(db.KeyStore.KeyFilePath)).Should().Equal(
            await File.ReadAllBytesAsync(other.KeyStore.KeyFilePath));

        await File.WriteAllBytesAsync(db.KeyStore.KeyFilePath, originalKeyFile);
        SqliteConnection.ClearAllPools();

        await db.CreateInitializer().InitializeAsync();
        CountSettings(db).Should().Be(1);
    }

    [Fact]
    public async Task Missing_key_file_fails_closed_and_is_not_recreated()
    {
        await using var db = new TempDatabase();
        await db.CreateInitializer().InitializeAsync();
        InsertSetting(db, Marker);

        var keyFile = await File.ReadAllBytesAsync(db.KeyStore.KeyFilePath);
        File.Delete(db.KeyStore.KeyFilePath);
        SqliteConnection.ClearAllPools();

        var act = () => new SqliteDatabaseInitializer(db.Options, db.Logger, new DatabaseKeyStore(db.Options))
            .InitializeAsync();

        var exception = (await act.Should().ThrowAsync<DatabaseKeyException>()).Which;

        exception.KeyFilePath.Should().Be(db.KeyStore.KeyFilePath);
        exception.Message.Should().Contain("key file is missing");
        File.Exists(db.KeyStore.KeyFilePath).Should().BeFalse();

        await File.WriteAllBytesAsync(db.KeyStore.KeyFilePath, keyFile);
        SqliteConnection.ClearAllPools();

        await db.CreateInitializer().InitializeAsync();
        CountSettings(db).Should().Be(1);
    }

    [Fact]
    public async Task Damaged_key_file_is_reported_and_never_overwritten()
    {
        await using var db = new TempDatabase();
        await db.CreateInitializer().InitializeAsync();
        InsertSetting(db, Marker);

        var damaged = "EDUFKEY1-damaged"u8.ToArray();
        await File.WriteAllBytesAsync(db.KeyStore.KeyFilePath, damaged);

        var act = () => new SqliteDatabaseInitializer(db.Options, db.Logger, new DatabaseKeyStore(db.Options))
            .InitializeAsync();

        var exception = (await act.Should().ThrowAsync<DatabaseKeyException>()).Which;

        exception.Message.Should().Contain("damaged");
        (await File.ReadAllBytesAsync(db.KeyStore.KeyFilePath)).Should().Equal(damaged);
    }

    [Fact]
    public async Task Key_file_stores_a_dpapi_protected_key_and_no_plaintext_key_material()
    {
        await using var db = new TempDatabase();

        db.KeyStore.LoadOrCreate();

        var fileBytes = File.ReadAllBytes(db.KeyStore.KeyFilePath);
        fileBytes.AsSpan(0, 8).ToArray().Should().Equal("EDUFKEY1"u8.ToArray());
        fileBytes.Length.Should().BeGreaterThanOrEqualTo(8 + 128);

        var rawKey = UnprotectKeyFile(fileBytes);
        rawKey.Should().HaveCount(32);
        IndexOf(fileBytes, rawKey).Should().Be(-1);
        Encoding.UTF8.GetString(fileBytes).Should().NotContain(Convert.ToHexString(rawKey));

        new DatabaseKeyStore(db.Options).Get().ToString().Should().Be("DatabaseEncryptionKey(redacted)");
    }

    [Fact]
    public async Task Plaintext_database_is_upgraded_in_place_with_data_preserved()
    {
        await using var db = new TempDatabase();

        await using (var context = db.CreatePlaintextContext())
            await context.GetService<IMigrator>().MigrateAsync();

        InsertSetting(db, Marker);
        SqliteCipher.GetFileState(db.DatabasePath).Should().Be(SqliteDatabaseFileState.Plaintext);

        var result = await db.CreateInitializer().InitializeAsync();

        result.BackupPath.Should().NotBeNullOrEmpty();
        SqliteCipher.GetFileState(db.DatabasePath).Should().Be(SqliteDatabaseFileState.Encrypted);
        SqliteCipher.GetFileState(result.BackupPath!).Should().Be(SqliteDatabaseFileState.Encrypted);
        db.CreateBackupStore().VerifyIntegrity(result.BackupPath!).Should().BeTrue();

        using (var connection = OpenWithoutKey(db.DatabasePath))
        {
            var read = () => TempDatabase.Scalar(connection, "SELECT COUNT(*) FROM sqlite_master;");
            read.Should().Throw<SqliteException>().Which.SqliteErrorCode.Should().Be(26);
        }

        CountSettings(db).Should().Be(1);

        using var reopened = db.OpenConnection();
        TempDatabase.Scalar(reopened, "SELECT Id FROM AppSettings;").Should().Be(Marker);
    }

    [Fact]
    public async Task Corrupt_plaintext_database_upgrade_keeps_the_original_file()
    {
        await using var db = new TempDatabase();
        var corruptBytes = "SQLite format 3\0this is not a real database"u8.ToArray();
        await File.WriteAllBytesAsync(db.DatabasePath, corruptBytes);

        var act = async () => await db.CreateInitializer().InitializeAsync();

        await act.Should().ThrowAsync<SqliteBackupException>();

        (await File.ReadAllBytesAsync(db.DatabasePath)).Should().Equal(corruptBytes);
        File.Exists(db.KeyStore.KeyFilePath).Should().BeTrue();
    }

    [Fact]
    public async Task Encrypted_backup_cannot_be_read_without_the_key_and_restores_data()
    {
        await using var db = new TempDatabase();
        await db.CreateInitializer().InitializeAsync();
        InsertSetting(db, Marker);

        var store = db.CreateBackupStore();
        var backupPath = store.CreateBackup();

        SqliteCipher.GetFileState(backupPath).Should().Be(SqliteDatabaseFileState.Encrypted);
        store.VerifyIntegrity(backupPath).Should().BeTrue();

        using (var connection = OpenWithoutKey(backupPath))
        {
            var read = () => TempDatabase.Scalar(connection, "SELECT COUNT(*) FROM sqlite_master;");
            read.Should().Throw<SqliteException>().Which.SqliteErrorCode.Should().Be(26);
        }

        db.CreateInitializer().RestoreBackup(backupPath);

        CountSettings(db).Should().Be(1);
        using var restored = db.OpenConnection();
        TempDatabase.Scalar(restored, "SELECT Id FROM AppSettings;").Should().Be(Marker);
    }

    [Fact]
    public async Task Legacy_plaintext_backup_is_encrypted_during_restore()
    {
        await using var db = new TempDatabase();
        await db.CreateInitializer().InitializeAsync();

        Directory.CreateDirectory(db.BackupDirectory);
        var legacyBackup = Path.Combine(db.BackupDirectory, "edufocus-20250101-000000000-00.db");

        using (var connection = OpenWithoutKey(legacyBackup, SqliteOpenMode.ReadWriteCreate))
        {
            TempDatabase.ExecuteNonQuery(
                connection,
                $"CREATE TABLE Legacy (Value TEXT); INSERT INTO Legacy VALUES ('{Marker}');");
        }

        SqliteCipher.GetFileState(legacyBackup).Should().Be(SqliteDatabaseFileState.Plaintext);

        var snapshot = db.CreateInitializer().RestoreBackup(legacyBackup);

        SqliteCipher.GetFileState(db.DatabasePath).Should().Be(SqliteDatabaseFileState.Encrypted);
        SqliteCipher.GetFileState(legacyBackup).Should().Be(SqliteDatabaseFileState.Plaintext);
        snapshot.Should().NotBeNull();
        SqliteCipher.GetFileState(snapshot!).Should().Be(SqliteDatabaseFileState.Encrypted);

        using var restored = db.OpenConnection();
        TempDatabase.Scalar(restored, "SELECT Value FROM Legacy;").Should().Be(Marker);
    }

    [Fact]
    public async Task Backup_encrypted_with_a_different_key_is_rejected()
    {
        await using var db = new TempDatabase();
        await db.CreateInitializer().InitializeAsync();
        InsertSetting(db, Marker);

        var backupPath = db.CreateBackupStore().CreateBackup();

        await using var other = new TempDatabase();
        other.KeyStore.LoadOrCreate();

        var storeWithWrongKey = new SqliteBackupStore(
            db.Options,
            db.Logger,
            new DatabaseKeyStore(other.Options));

        var act = () => storeWithWrongKey.Restore(backupPath);

        act.Should().Throw<SqliteBackupException>().WithMessage("*different key*");

        CountSettings(db).Should().Be(1);
        db.CreateBackupStore().VerifyIntegrity(db.DatabasePath).Should().BeTrue();
    }

    [Fact]
    public async Task Logs_never_contain_the_encryption_key()
    {
        await using var db = new TempDatabase();
        await db.CreateInitializer().InitializeAsync();
        InsertSetting(db, Marker);
        db.CreateBackupStore().CreateBackup();

        var rawKey = UnprotectKeyFile(await File.ReadAllBytesAsync(db.KeyStore.KeyFilePath));
        var hex = Convert.ToHexString(rawKey);
        var base64 = Convert.ToBase64String(rawKey);
        var rawKeyText = Encoding.UTF8.GetString(rawKey);

        var messages = db.Logger.Infos
            .Concat(db.Logger.Warnings)
            .Concat(db.Logger.Errors)
            .Concat(db.Logger.Exceptions.Where(e => e is not null).Select(e => e!.Message))
            .ToList();

        messages.Should().NotBeEmpty();
        messages.Should().NotContain(
            message => message.Contains(hex, StringComparison.OrdinalIgnoreCase)
                || message.Contains("x'" + hex, StringComparison.OrdinalIgnoreCase)
                || message.Contains(base64, StringComparison.Ordinal)
                || message.Contains(rawKeyText, StringComparison.Ordinal));
    }

    [Fact]
    public async Task Wal_and_database_files_contain_no_plaintext()
    {
        await using var db = new TempDatabase();
        await db.CreateInitializer().InitializeAsync();

        using (var connection = db.OpenConnection())
        {
            TempDatabase.ExecuteNonQuery(connection, "PRAGMA journal_mode = WAL;");
            InsertSetting(connection, Marker);
            TempDatabase.Scalar(connection, "SELECT Id FROM AppSettings;").Should().Be(Marker);

            foreach (var path in new[]
                     {
                         db.DatabasePath,
                         db.DatabasePath + "-wal",
                         db.DatabasePath + "-shm"
                     })
            {
                if (!File.Exists(path))
                    continue;

                IndexOf(await ReadAllBytesSharedAsync(path), Encoding.UTF8.GetBytes(Marker)).Should().Be(-1);
            }
        }
    }

    private static SqliteConnection OpenWithoutKey(
        string path,
        SqliteOpenMode mode = SqliteOpenMode.ReadWrite)
    {
        var connection = new SqliteConnection(
            new SqliteConnectionStringBuilder
            {
                DataSource = path,
                Mode = mode,
                Pooling = false
            }.ToString());
        connection.Open();
        return connection;
    }

    private static async Task<byte[]> ReadAllBytesSharedAsync(string path)
    {
        await using var stream = new FileStream(
            path,
            FileMode.Open,
            FileAccess.Read,
            FileShare.ReadWrite | FileShare.Delete);
        using var buffer = new MemoryStream();
        await stream.CopyToAsync(buffer);
        return buffer.ToArray();
    }

    private static byte[] ReadHeader(string path)
    {
        using var stream = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite);
        var header = new byte[16];
        stream.ReadExactly(header);
        return header;
    }

    private static byte[] UnprotectKeyFile(byte[] fileBytes)
    {
        if (!OperatingSystem.IsWindows())
            throw new PlatformNotSupportedException();

        return ProtectedData.Unprotect(
            fileBytes.AsSpan(8).ToArray(),
            Encoding.UTF8.GetBytes("EduFocus.DatabaseKey.v1"),
            DataProtectionScope.LocalMachine);
    }

    private static int IndexOf(byte[] haystack, byte[] needle)
    {
        for (var i = 0; i <= haystack.Length - needle.Length; i++)
        {
            if (haystack.AsSpan(i, needle.Length).SequenceEqual(needle))
                return i;
        }

        return -1;
    }

    private static void InsertSetting(TempDatabase db, string id)
    {
        using var connection = db.OpenConnection();
        InsertSetting(connection, id);
    }

    private static void InsertSetting(SqliteConnection connection, string id) =>
        TempDatabase.ExecuteNonQuery(
            connection,
            $"INSERT INTO AppSettings (Id, Theme, CreatedAtUtc, UpdatedAtUtc) VALUES ('{id}', 'dark', '2026-01-01T00:00:00.000Z', '2026-01-01T00:00:00.000Z');");

    private static long CountSettings(TempDatabase db)
    {
        using var connection = db.OpenConnection();
        return (long)TempDatabase.Scalar(connection, "SELECT COUNT(*) FROM AppSettings;")!;
    }
}
