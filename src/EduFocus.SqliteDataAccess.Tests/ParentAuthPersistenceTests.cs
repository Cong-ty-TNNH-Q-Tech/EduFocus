using System.Text;
using EduFocus.Domain.Entities;
using EduFocus.Domain.Enums;
using EduFocus.Domain.Security;
using EduFocus.Infrastructure.Auth;
using EduFocus.Infrastructure.Logging;
using EduFocus.Ports.DTOs;
using EduFocus.Ports.Outbound.Services;
using EduFocus.SqliteDataAccess.Persistence;
using EduFocus.SqliteDataAccess.Repositories;
using EduFocus.SqliteDataAccess.Tests.TestSupport;
using EduFocus.UseCases.Auth;
using FluentAssertions;
using Microsoft.EntityFrameworkCore;

namespace EduFocus.SqliteDataAccess.Tests;

public sealed class ParentAuthPersistenceTests
{
    [Fact]
    public async Task Setup_login_and_recovery_survive_a_process_restart()
    {
        await using var db = new TempDatabase();
        await db.CreateInitializer().InitializeAsync();

        var correlationId = Guid.NewGuid();
        var correlation = new CorrelationContext();
        using var scope = correlation.BeginScope(correlationId);

        var useCase = CreateUseCase(db, correlation);
        var setup = await useCase.SetupAsync("482915");

        ParentRecoveryCode.IsValid(setup.RecoveryCode).Should().BeTrue();

        var stored = await CreateRepository(db).GetAsync();
        stored.Should().NotBeNull();
        stored!.PinHash.Should().NotBe("482915");
        stored.PinIterations.Should().Be(Pbkdf2PasswordHasher.DefaultIterations);
        stored.RecoveryCodeHash.Should().NotBe(setup.RecoveryCode);
        stored.RecoveryCodeIterations.Should().Be(Pbkdf2PasswordHasher.DefaultIterations);
        Convert.FromBase64String(stored.PinHash).Should().HaveCount(Pbkdf2PasswordHasher.HashSizeBytes);
        Convert.FromBase64String(stored.PinSalt).Should().HaveCount(Pbkdf2PasswordHasher.SaltSizeBytes);
        Convert.FromBase64String(stored.RecoveryCodeHash).Should().HaveCount(Pbkdf2PasswordHasher.HashSizeBytes);
        Convert.FromBase64String(stored.RecoveryCodeSalt).Should().HaveCount(Pbkdf2PasswordHasher.SaltSizeBytes);

        (await useCase.VerifyPinAsync("482915")).Status.Should().Be(ParentAuthStatus.Success);
        (await useCase.VerifyPinAsync("482916")).Status.Should().Be(ParentAuthStatus.InvalidCredential);

        var restarted = CreateUseCase(db, correlation);
        (await restarted.VerifyPinAsync("482915")).Status.Should().Be(ParentAuthStatus.Success);

        var beforeChange = await CreateRepository(db).GetAsync();
        var change = await restarted.ChangePinAsync("482915", "135790");
        var afterChange = await CreateRepository(db).GetAsync();

        change.Status.Should().Be(ParentAuthStatus.Success);
        afterChange!.PinHash.Should().NotBe(beforeChange!.PinHash);
        afterChange.PinSalt.Should().NotBe(beforeChange.PinSalt);
        (await restarted.VerifyPinAsync("482915")).Status.Should().Be(ParentAuthStatus.InvalidCredential);
        (await restarted.VerifyPinAsync("135790")).Status.Should().Be(ParentAuthStatus.Success);

        var reset = await restarted.ResetPinWithRecoveryCodeAsync(setup.RecoveryCode, "246813");
        reset.Status.Should().Be(ParentAuthStatus.Success);
        reset.RecoveryCode.Should().NotBeNull();
        reset.RecoveryCode.Should().NotBe(setup.RecoveryCode);

        (await restarted.VerifyPinAsync("135790")).Status.Should().Be(ParentAuthStatus.InvalidCredential);
        (await restarted.VerifyPinAsync("246813")).Status.Should().Be(ParentAuthStatus.Success);
        (await restarted.ResetPinWithRecoveryCodeAsync(setup.RecoveryCode, "482915")).Status
            .Should().Be(ParentAuthStatus.InvalidCredential);

        await using var context = db.CreateContext();
        (await context.ParentCredentials.CountAsync()).Should().Be(1);

        var audits = await context.AuditLogs.AsNoTracking().ToListAsync();
        var events = audits.Select(x => x.EventType).ToList();
        events.Should().Contain(AuditEventType.PinSetup);
        events.Should().Contain(AuditEventType.ParentLogin);
        events.Should().Contain(AuditEventType.AuthenticationFailed);
        events.Should().Contain(AuditEventType.RecoveryCodeUsed);
        events.Should().Contain(AuditEventType.PinChanged);
        audits.Should().OnlyContain(x => x.CorrelationId == correlationId);

        var auditText = string.Join("\n", audits.Select(x => $"{x.Description} {x.MetadataJson}"));
        auditText.Should().NotContain("482915");
        auditText.Should().NotContain("482916");
        auditText.Should().NotContain("135790");
        auditText.Should().NotContain("246813");
        auditText.Should().NotContain(setup.RecoveryCode);
    }

    [Fact]
    public async Task Database_wal_and_backup_files_never_contain_plaintext_secrets()
    {
        await using var db = new TempDatabase();
        await db.CreateInitializer().InitializeAsync();

        var recoveryCodes = new DeterministicRecoveryCodeGenerator(
            "ZZZZZZZZZZZZ",
            "YYYYYYYYYYYY");
        var useCase = CreateUseCase(
            db,
            new CorrelationContext(),
            recoveryCodeGenerator: recoveryCodes);

        var setup = await useCase.SetupAsync("91827364");
        setup.RecoveryCode.Should().Be("ZZZZZZZZZZZZ");
        (await useCase.VerifyPinAsync("91827364")).Status.Should().Be(ParentAuthStatus.Success);
        (await useCase.VerifyPinAsync("57392018")).Status.Should().Be(ParentAuthStatus.InvalidCredential);
        (await useCase.ChangePinAsync("91827364", "57392018")).Status.Should().Be(ParentAuthStatus.Success);

        var reset = await useCase.ResetPinWithRecoveryCodeAsync("ZZZZZZZZZZZZ", "60492731");
        reset.Status.Should().Be(ParentAuthStatus.Success);
        reset.RecoveryCode.Should().Be("YYYYYYYYYYYY");

        // Keep a connection open so the WAL file exists while it is inspected.
        using var keepAlive = db.OpenConnectionWithPragmas();
        var backupPath = new SqliteBackupStore(db.Options, db.Logger).CreateBackup();

        var files = Directory
            .GetFiles(db.RootPath, "*", SearchOption.AllDirectories)
            .ToList();
        files.Should().Contain(Path.GetFullPath(db.DatabasePath));
        files.Should().Contain(Path.GetFullPath(backupPath));
        File.Exists(db.DatabasePath + "-wal").Should().BeTrue(
            "the test must inspect the write-ahead log as well as the main database");

        string[] secrets = ["91827364", "57392018", "60492731", "ZZZZZZZZZZZZ", "YYYYYYYYYYYY"];
        foreach (var file in files)
        {
            var bytes = await ReadSharedAsync(file);
            foreach (var secret in secrets)
            {
                ContainsAscii(bytes, secret).Should().BeFalse(
                    $"{Path.GetFileName(file)} must not contain a plaintext PIN or recovery code");
            }
        }
    }

    [Fact]
    public async Task Lockout_survives_a_restart_and_expires_after_fifteen_minutes()
    {
        await using var db = new TempDatabase();
        await db.CreateInitializer().InitializeAsync();

        var correlation = new CorrelationContext();
        var clock = new AdjustableTimeProvider(new DateTime(2026, 10, 29, 8, 0, 0, DateTimeKind.Utc));
        var useCase = CreateUseCase(db, correlation, clock);
        await useCase.SetupAsync("482915");

        ParentAuthResult? lastFailure = null;
        for (var attempt = 1; attempt <= ParentCredential.MaxFailedAttempts; attempt++)
        {
            lastFailure = await useCase.VerifyPinAsync("482916");
            lastFailure.Status.Should().Be(
                attempt == ParentCredential.MaxFailedAttempts
                    ? ParentAuthStatus.LockedOut
                    : ParentAuthStatus.InvalidCredential);
        }

        lastFailure!.RemainingAttempts.Should().Be(0);
        lastFailure.LockoutUntilUtc.Should()
            .Be(clock.GetUtcNow().UtcDateTime.Add(ParentCredential.LockoutDuration));

        // A new process keeps only the database, so a second clock instance
        // set to the same wall time proves the lockout comes from storage.
        var restartedClock = new AdjustableTimeProvider(clock.GetUtcNow().UtcDateTime.AddMinutes(5));
        var restarted = CreateUseCase(db, correlation, restartedClock);

        var stillLocked = await restarted.VerifyPinAsync("482915");
        stillLocked.Status.Should().Be(ParentAuthStatus.LockedOut);
        stillLocked.RemainingAttempts.Should().Be(0);

        restartedClock.Advance(TimeSpan.FromMinutes(10));
        var afterExpiry = await restarted.VerifyPinAsync("482915");
        afterExpiry.Status.Should().Be(ParentAuthStatus.Success);

        await using var context = db.CreateContext();
        var stored = await context.ParentCredentials.AsNoTracking().SingleAsync();
        stored.FailedAttempts.Should().Be(0);
        stored.LockoutUntilUtc.Should().BeNull();

        var events = await context.AuditLogs.AsNoTracking()
            .Select(x => x.EventType)
            .ToListAsync();
        events.Count(x => x == AuditEventType.AuthenticationFailed).Should()
            .Be(ParentCredential.MaxFailedAttempts);
        events.Count(x => x == AuditEventType.LockoutTriggered).Should().Be(1);
    }

    private static ParentAuthUseCase CreateUseCase(
        TempDatabase db,
        CorrelationContext correlation,
        TimeProvider? timeProvider = null,
        IRecoveryCodeGenerator? recoveryCodeGenerator = null) =>
        new(
            CreateRepository(db),
            new Pbkdf2PasswordHasher(),
            recoveryCodeGenerator ?? new RecoveryCodeGenerator(),
            new SqliteAuditLogRepository(db.Options),
            correlation,
            timeProvider ?? TimeProvider.System);

    private static SqliteParentCredentialRepository CreateRepository(TempDatabase db) =>
        new(db.Options);

    private static bool ContainsAscii(byte[] bytes, string value) =>
        bytes.AsSpan().IndexOf(Encoding.ASCII.GetBytes(value)) >= 0;

    private static async Task<byte[]> ReadSharedAsync(string path)
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

    private sealed class DeterministicRecoveryCodeGenerator(params string[] codes)
        : IRecoveryCodeGenerator
    {
        private int _index;

        public string Generate() => codes[_index++ % codes.Length];
    }
}
