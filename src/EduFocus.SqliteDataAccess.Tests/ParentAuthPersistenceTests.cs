using EduFocus.Domain.Enums;
using EduFocus.Domain.Security;
using EduFocus.Infrastructure.Auth;
using EduFocus.Infrastructure.Logging;
using EduFocus.Ports.DTOs;
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

    private static ParentAuthUseCase CreateUseCase(TempDatabase db, CorrelationContext correlation) =>
        new(
            CreateRepository(db),
            new Pbkdf2PasswordHasher(),
            new RecoveryCodeGenerator(),
            new SqliteAuditLogRepository(db.Options),
            correlation,
            TimeProvider.System);

    private static SqliteParentCredentialRepository CreateRepository(TempDatabase db) =>
        new(db.Options);
}
