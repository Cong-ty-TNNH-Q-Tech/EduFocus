using EduFocus.Domain.Entities;
using EduFocus.Infrastructure.Persistence;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;

namespace EduFocus.Infrastructure.Persistence.Tests;

public sealed class ParentCredentialServiceTests
{
    private static readonly DateTimeOffset Now = new(2026, 10, 26, 10, 0, 0, TimeSpan.Zero);

    [Fact]
    public async Task Setup_stores_only_salted_hashes_and_returns_recovery_code_once()
    {
        await using var fixture = await Fixture.Create();
        var result = await fixture.Service.SetUpAsync("482951", Now);
        Assert.Equal(CredentialOperationStatus.Succeeded, result.Status);
        Assert.Matches("^[A-HJ-NP-Z2-9]{16}$", result.RecoveryCode!);

        var stored = await fixture.Db.ParentCredentials.SingleAsync();
        Assert.NotEqual("482951", stored.Pin.HashBase64);
        Assert.NotEqual(result.RecoveryCode, stored.RecoveryCode.HashBase64);
        Assert.Equal(16, Convert.FromBase64String(stored.Pin.SaltBase64).Length);
        Assert.Equal(32, Convert.FromBase64String(stored.Pin.HashBase64).Length);
        Assert.Equal(600_000, stored.Pin.Iterations);
        var columns = await ReadCredentialColumns(fixture.Connection);
        Assert.DoesNotContain("482951", columns);
        Assert.DoesNotContain(result.RecoveryCode!, columns);
        Assert.Equal(CredentialOperationStatus.Succeeded, (await fixture.Service.AuthenticatePinAsync("482951", Now.AddSeconds(1))).Status);
        Assert.True(stored.RecoveryCode.VerifySecret(result.RecoveryCode!));
    }

    [Fact]
    public async Task Five_failed_attempts_lock_both_methods_for_fifteen_minutes_and_success_resets()
    {
        await using var fixture = await Fixture.Create();
        await fixture.Service.SetUpAsync("482951", Now);
        for (var i = 0; i < 4; i++)
            Assert.Equal(CredentialOperationStatus.InvalidCredential, (await fixture.Service.AuthenticatePinAsync("000000", Now.AddMinutes(i))).Status);
        Assert.Equal(CredentialOperationStatus.InvalidCredential, (await fixture.Service.RecoverPinAsync("BADCODE", "739204", Now.AddMinutes(4))).Status);
        Assert.Equal(CredentialOperationStatus.LockedOut, (await fixture.Service.AuthenticatePinAsync("482951", Now.AddMinutes(4).AddSeconds(1))).Status);
        Assert.Equal(Now.AddMinutes(19), (await fixture.Db.ParentCredentials.SingleAsync()).LockoutUntilUtc);

        var reopenedOptions = new DbContextOptionsBuilder<AppDbContext>().UseSqlite(fixture.Connection).Options;
        await using (var reopenedDb = new AppDbContext(reopenedOptions))
        {
            var persistedStatus = await new ParentCredentialService(reopenedDb).GetLockoutStatusAsync(Now.AddMinutes(4).AddSeconds(1));
            Assert.True(persistedStatus.IsLockedOut);
            Assert.Equal(5, persistedStatus.FailedAttempts);
            Assert.Equal(TimeSpan.FromMinutes(14).Add(TimeSpan.FromSeconds(59)), persistedStatus.Remaining);
        }

        Assert.Equal(CredentialOperationStatus.InvalidCredential, (await fixture.Service.AuthenticatePinAsync("000000", Now.AddMinutes(20))).Status);
        Assert.Equal(1, (await fixture.Db.ParentCredentials.SingleAsync()).FailedAttempts);
        Assert.Equal(CredentialOperationStatus.Succeeded, (await fixture.Service.AuthenticatePinAsync("482951", Now.AddMinutes(21))).Status);
        var credentials = await fixture.Db.ParentCredentials.SingleAsync();
        Assert.Equal(0, credentials.FailedAttempts);
        Assert.Null(credentials.LockoutUntilUtc);
    }

    [Fact]
    public async Task Changing_pin_uses_fresh_salt_invalidates_old_pin_and_audits_without_secrets()
    {
        await using var fixture = await Fixture.Create();
        await fixture.Service.SetUpAsync("482951", Now);
        var oldSalt = (await fixture.Db.ParentCredentials.SingleAsync()).Pin.SaltBase64;
        Assert.Equal(CredentialOperationStatus.Succeeded, (await fixture.Service.ChangePinAsync("482951", "739204", Now.AddMinutes(1))).Status);
        var stored = await fixture.Db.ParentCredentials.SingleAsync();
        Assert.NotEqual(oldSalt, stored.Pin.SaltBase64);
        Assert.Equal(CredentialOperationStatus.InvalidCredential, (await fixture.Service.AuthenticatePinAsync("482951", Now.AddMinutes(2))).Status);
        Assert.Equal(CredentialOperationStatus.Succeeded, (await fixture.Service.AuthenticatePinAsync("739204", Now.AddMinutes(3))).Status);
        var audit = await fixture.Db.AuditLogs.Select(x => x.Description).ToArrayAsync();
        Assert.DoesNotContain(audit, x => x.Contains("482951", StringComparison.Ordinal) || x.Contains("739204", StringComparison.Ordinal));
        var events = await fixture.Db.AuditLogs.ToArrayAsync();
        Assert.Contains(events, x => x.EventType == "PinChanged");
        Assert.Contains(events, x => x.EventType == "ParentLogin");
        Assert.All(events, x => Assert.DoesNotContain("Hash", x.MetadataJson ?? string.Empty, StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public async Task Recovery_rotates_code_and_old_code_cannot_be_reused()
    {
        await using var fixture = await Fixture.Create();
        var initial = (await fixture.Service.SetUpAsync("482951", Now)).RecoveryCode!;
        var recovered = await fixture.Service.RecoverPinAsync(initial, "739204", Now.AddMinutes(1));
        Assert.Equal(CredentialOperationStatus.Succeeded, recovered.Status);
        Assert.NotEqual(initial, recovered.RecoveryCode);
        Assert.Equal(CredentialOperationStatus.InvalidCredential, (await fixture.Service.RecoverPinAsync(initial, "827415", Now.AddMinutes(2))).Status);
        var secondRecovery = await fixture.Service.RecoverPinAsync(recovered.RecoveryCode!, "827415", Now.AddMinutes(3));
        Assert.Equal(CredentialOperationStatus.Succeeded, secondRecovery.Status);
        Assert.Equal(CredentialOperationStatus.InvalidCredential, (await fixture.Service.AuthenticatePinAsync("739204", Now.AddMinutes(4))).Status);
        Assert.Equal(CredentialOperationStatus.Succeeded, (await fixture.Service.AuthenticatePinAsync("827415", Now.AddMinutes(5))).Status);
        Assert.Contains(await fixture.Db.AuditLogs.ToArrayAsync(), x => x.EventType == "PinRecovered");
    }

    private static async Task<string> ReadCredentialColumns(SqliteConnection connection)
    {
        await using var command = connection.CreateCommand();
        command.CommandText = "SELECT PinHash, PinSalt, PinIterations, RecoveryCodeHash, RecoveryCodeSalt, RecoveryCodeIterations FROM ParentCredentials";
        await using var reader = await command.ExecuteReaderAsync();
        Assert.True(await reader.ReadAsync());
        var values = Enumerable.Range(0, reader.FieldCount).Select(i => reader.GetValue(i)).ToArray();
        return string.Join("|", values);
    }

    private sealed class Fixture(SqliteConnection connection, AppDbContext db) : IAsyncDisposable
    {
        public SqliteConnection Connection { get; } = connection;
        public AppDbContext Db { get; } = db;
        public ParentCredentialService Service { get; } = new(db);

        public static async Task<Fixture> Create()
        {
            var connection = new SqliteConnection("Data Source=:memory:");
            await connection.OpenAsync();
            var options = new DbContextOptionsBuilder<AppDbContext>().UseSqlite(connection).Options;
            var db = new AppDbContext(options);
            await db.Database.MigrateAsync();
            return new(connection, db);
        }

        public async ValueTask DisposeAsync()
        {
            await Db.DisposeAsync();
            await Connection.DisposeAsync();
        }
    }
}
