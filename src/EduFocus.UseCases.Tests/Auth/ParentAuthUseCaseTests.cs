using EduFocus.Domain.Enums;
using EduFocus.Ports.DTOs;
using EduFocus.Ports.Outbound.Services;
using EduFocus.UseCases.Tests.TestSupport;
using FluentAssertions;

namespace EduFocus.UseCases.Tests.Auth;

public sealed class ParentAuthUseCaseTests
{
    [Fact]
    public async Task Setup_persists_only_hashes_and_returns_recovery_code()
    {
        var harness = new AuthHarness();

        var result = await harness.UseCase.SetupAsync(AuthHarness.InitialPin);

        harness.Credentials.AddCount.Should().Be(1);
        result.RecoveryCode.Should().Be(AuthHarness.FirstRecoveryCode);
        harness.Credential.PinHash.Should().NotBe(AuthHarness.InitialPin);
        harness.Credential.PinIterations.Should().Be(600_000);
        harness.Hasher.Verify(
                AuthHarness.InitialPin,
                new SecretHash(
                    harness.Credential.PinHash,
                    harness.Credential.PinSalt,
                    harness.Credential.PinIterations))
            .Should()
            .BeTrue();
        harness.Hasher.Verify(
                AuthHarness.FirstRecoveryCode,
                new SecretHash(
                    harness.Credential.RecoveryCodeHash,
                    harness.Credential.RecoveryCodeSalt,
                    harness.Credential.RecoveryCodeIterations))
            .Should()
            .BeTrue();
        harness.Credential.CreatedAtUtc.Should().Be(AuthHarness.Start.UtcDateTime);

        var audit = harness.Audits.Entries.Should().ContainSingle().Subject;
        audit.EventType.Should().Be(AuditEventType.PinSetup);
        audit.Actor.Should().Be(AuditActor.Parent);
        audit.CorrelationId.Should().Be(AuthHarness.CorrelationId);
    }

    [Fact]
    public async Task IsConfiguredAsync_reflects_credential_presence()
    {
        var harness = new AuthHarness();

        (await harness.UseCase.IsConfiguredAsync()).Should().BeFalse();

        await harness.UseCase.SetupAsync(AuthHarness.InitialPin);

        (await harness.UseCase.IsConfiguredAsync()).Should().BeTrue();
    }

    [Fact]
    public async Task Setup_rejects_a_weak_pin()
    {
        var harness = new AuthHarness();

        var act = () => harness.UseCase.SetupAsync("111111");

        await act.Should().ThrowAsync<ArgumentException>();
        harness.Credentials.AddCount.Should().Be(0);
        harness.Audits.Entries.Should().BeEmpty();
    }

    [Fact]
    public async Task Setup_when_already_configured_throws()
    {
        var harness = await AuthHarness.WithCredentialAsync();

        var act = () => harness.UseCase.SetupAsync("135790");

        await act.Should().ThrowAsync<InvalidOperationException>();
        harness.Credentials.AddCount.Should().Be(1);
    }

    [Fact]
    public async Task Operations_report_not_configured_without_a_credential()
    {
        var harness = new AuthHarness();

        var login = await harness.UseCase.VerifyPinAsync(AuthHarness.InitialPin);
        var change = await harness.UseCase.ChangePinAsync(AuthHarness.InitialPin, AuthHarness.ReplacementPin);
        var reset = await harness.UseCase.ResetPinWithRecoveryCodeAsync(
            AuthHarness.FirstRecoveryCode,
            AuthHarness.ReplacementPin);

        login.Status.Should().Be(ParentAuthStatus.NotConfigured);
        change.Status.Should().Be(ParentAuthStatus.NotConfigured);
        reset.Status.Should().Be(ParentAuthStatus.NotConfigured);
        reset.RecoveryCode.Should().BeNull();
        harness.Audits.Entries.Should().BeEmpty();
    }

    [Fact]
    public async Task VerifyPin_with_correct_pin_clears_previous_failures()
    {
        var harness = await AuthHarness.WithCredentialAsync();
        await harness.UseCase.VerifyPinAsync("482916");
        await harness.UseCase.VerifyPinAsync("482916");

        var result = await harness.UseCase.VerifyPinAsync(AuthHarness.InitialPin);

        result.Status.Should().Be(ParentAuthStatus.Success);
        result.RemainingAttempts.Should().Be(5);
        result.LockoutUntilUtc.Should().BeNull();
        harness.Credential.FailedAttempts.Should().Be(0);
        harness.Audits.Entries.Should().ContainSingle(x => x.EventType == AuditEventType.ParentLogin);
    }

    [Fact]
    public async Task VerifyPin_with_wrong_pin_counts_and_audits_each_failure()
    {
        var harness = await AuthHarness.WithCredentialAsync();

        var results = new List<ParentAuthResult>();
        for (var attempt = 0; attempt < 4; attempt++)
            results.Add(await harness.UseCase.VerifyPinAsync("482916"));

        results.Select(x => x.Status).Should().OnlyContain(x => x == ParentAuthStatus.InvalidCredential);
        results.Select(x => x.RemainingAttempts).Should().Equal(4, 3, 2, 1);
        results.Select(x => x.LockoutUntilUtc).Should().OnlyContain(x => x == null);
        harness.Credential.FailedAttempts.Should().Be(4);

        var failures = harness.Audits.Entries
            .Where(x => x.EventType == AuditEventType.AuthenticationFailed)
            .ToList();
        failures.Should().HaveCount(4);
        failures.Should().OnlyContain(x => x.Description.Contains("PIN"));
        failures.Select(x => x.MetadataJson).Should().OnlyContain(x => !x!.Contains("482916"));
    }

    [Fact]
    public async Task Fifth_wrong_pin_locks_for_fifteen_minutes_and_audits_lockout()
    {
        var harness = await AuthHarness.WithCredentialAsync();

        ParentAuthResult result = null!;
        for (var attempt = 0; attempt < 5; attempt++)
            result = await harness.UseCase.VerifyPinAsync("482916");

        result.Status.Should().Be(ParentAuthStatus.LockedOut);
        result.RemainingAttempts.Should().Be(0);
        result.LockoutUntilUtc.Should().Be(AuthHarness.Start.AddMinutes(15).UtcDateTime);
        harness.Credential.FailedAttempts.Should().Be(5);
        harness.Credential.LockoutUntilUtc.Should().Be(result.LockoutUntilUtc);

        harness.Audits.Entries.Count(x => x.EventType == AuditEventType.AuthenticationFailed)
            .Should().Be(5);
        var lockout = harness.Audits.Entries
            .Should().ContainSingle(x => x.EventType == AuditEventType.LockoutTriggered).Subject;
        lockout.MetadataJson.Should().Contain("lockoutUntilUtc");
    }

    [Fact]
    public async Task VerifyPin_while_locked_does_not_count_or_audit()
    {
        var harness = await AuthHarness.WithCredentialAsync();
        for (var attempt = 0; attempt < 5; attempt++)
            await harness.UseCase.VerifyPinAsync("482916");

        var updateCount = harness.Credentials.UpdateCount;
        var auditCount = harness.Audits.Entries.Count;
        harness.Clock.Advance(TimeSpan.FromMinutes(1));

        var result = await harness.UseCase.VerifyPinAsync(AuthHarness.InitialPin);

        result.Status.Should().Be(ParentAuthStatus.LockedOut);
        result.LockoutUntilUtc.Should().Be(AuthHarness.Start.AddMinutes(15).UtcDateTime);
        harness.Credential.FailedAttempts.Should().Be(5);
        harness.Credentials.UpdateCount.Should().Be(updateCount);
        harness.Audits.Entries.Should().HaveCount(auditCount);
    }

    [Fact]
    public async Task Lockout_expiry_starts_a_fresh_failure_count()
    {
        var harness = await AuthHarness.WithCredentialAsync();
        for (var attempt = 0; attempt < 5; attempt++)
            await harness.UseCase.VerifyPinAsync("482916");

        harness.Clock.Advance(TimeSpan.FromMinutes(15) + TimeSpan.FromSeconds(1));

        var result = await harness.UseCase.VerifyPinAsync("482916");

        result.Status.Should().Be(ParentAuthStatus.InvalidCredential);
        result.RemainingAttempts.Should().Be(4);
        result.LockoutUntilUtc.Should().BeNull();
        harness.Credential.FailedAttempts.Should().Be(1);
    }

    [Fact]
    public async Task Lockout_expiry_allows_a_correct_pin_and_clears_state()
    {
        var harness = await AuthHarness.WithCredentialAsync();
        for (var attempt = 0; attempt < 5; attempt++)
            await harness.UseCase.VerifyPinAsync("482916");

        harness.Clock.Advance(TimeSpan.FromMinutes(15) + TimeSpan.FromSeconds(1));

        var result = await harness.UseCase.VerifyPinAsync(AuthHarness.InitialPin);

        result.Status.Should().Be(ParentAuthStatus.Success);
        harness.Credential.FailedAttempts.Should().Be(0);
        harness.Credential.LockoutUntilUtc.Should().BeNull();
    }

    [Fact]
    public async Task ChangePin_with_the_current_pin_rehashes_and_resets_attempts()
    {
        var harness = await AuthHarness.WithCredentialAsync();
        await harness.UseCase.VerifyPinAsync("482916");
        await harness.UseCase.VerifyPinAsync("482916");
        var previousHash = harness.Credential.PinHash;

        var result = await harness.UseCase.ChangePinAsync(
            AuthHarness.InitialPin,
            AuthHarness.ReplacementPin);

        result.Status.Should().Be(ParentAuthStatus.Success);
        harness.Credential.PinHash.Should().NotBe(previousHash);
        harness.Credential.FailedAttempts.Should().Be(0);
        harness.Hasher.Verify(
                AuthHarness.ReplacementPin,
                new SecretHash(
                    harness.Credential.PinHash,
                    harness.Credential.PinSalt,
                    harness.Credential.PinIterations))
            .Should()
            .BeTrue();
        harness.Audits.Entries.Should().ContainSingle(x => x.EventType == AuditEventType.PinChanged);

        var oldPin = await harness.UseCase.VerifyPinAsync(AuthHarness.InitialPin);
        oldPin.Status.Should().Be(ParentAuthStatus.InvalidCredential);
    }

    [Fact]
    public async Task ChangePin_with_a_wrong_current_pin_counts_as_a_failure()
    {
        var harness = await AuthHarness.WithCredentialAsync();
        var previousHash = harness.Credential.PinHash;

        var result = await harness.UseCase.ChangePinAsync("482916", AuthHarness.ReplacementPin);

        result.Status.Should().Be(ParentAuthStatus.InvalidCredential);
        result.RemainingAttempts.Should().Be(4);
        harness.Credential.FailedAttempts.Should().Be(1);
        harness.Credential.PinHash.Should().Be(previousHash);
        harness.Audits.Entries.Should().ContainSingle(x => x.EventType == AuditEventType.AuthenticationFailed);
    }

    [Fact]
    public async Task ChangePin_rejects_a_weak_new_pin_without_consuming_an_attempt()
    {
        var harness = await AuthHarness.WithCredentialAsync();
        await harness.UseCase.VerifyPinAsync("482916");
        var updateCount = harness.Credentials.UpdateCount;

        var act = () => harness.UseCase.ChangePinAsync(AuthHarness.InitialPin, "123456");

        await act.Should().ThrowAsync<ArgumentException>();
        harness.Credential.FailedAttempts.Should().Be(1);
        harness.Credentials.UpdateCount.Should().Be(updateCount);
    }

    [Fact]
    public async Task Reset_with_a_recovery_code_rotates_pin_and_recovery_code()
    {
        var harness = await AuthHarness.WithCredentialAsync();
        await harness.UseCase.VerifyPinAsync("482916");

        var result = await harness.UseCase.ResetPinWithRecoveryCodeAsync(
            AuthHarness.FirstRecoveryCode,
            AuthHarness.ReplacementPin);

        result.Status.Should().Be(ParentAuthStatus.Success);
        result.RecoveryCode.Should().Be(AuthHarness.SecondRecoveryCode);
        harness.Credential.FailedAttempts.Should().Be(0);
        harness.Hasher.Verify(
                AuthHarness.ReplacementPin,
                new SecretHash(
                    harness.Credential.PinHash,
                    harness.Credential.PinSalt,
                    harness.Credential.PinIterations))
            .Should()
            .BeTrue();
        harness.Hasher.Verify(
                AuthHarness.SecondRecoveryCode,
                new SecretHash(
                    harness.Credential.RecoveryCodeHash,
                    harness.Credential.RecoveryCodeSalt,
                    harness.Credential.RecoveryCodeIterations))
            .Should()
            .BeTrue();
        harness.Hasher.Verify(
                AuthHarness.FirstRecoveryCode,
                new SecretHash(
                    harness.Credential.RecoveryCodeHash,
                    harness.Credential.RecoveryCodeSalt,
                    harness.Credential.RecoveryCodeIterations))
            .Should()
            .BeFalse();

        harness.Audits.Entries.Should().ContainSingle(x => x.EventType == AuditEventType.RecoveryCodeUsed);
        harness.Audits.Entries.Should().ContainSingle(x => x.EventType == AuditEventType.PinChanged);
    }

    [Fact]
    public async Task Recovery_code_failures_share_the_pin_lockout()
    {
        var harness = await AuthHarness.WithCredentialAsync();

        ParentPinResetResult result = null!;
        for (var attempt = 0; attempt < 5; attempt++)
        {
            result = await harness.UseCase.ResetPinWithRecoveryCodeAsync(
                "ZZZZZZZZZZZZ",
                AuthHarness.ReplacementPin);
        }

        result.Status.Should().Be(ParentAuthStatus.LockedOut);
        result.RemainingAttempts.Should().Be(0);
        result.RecoveryCode.Should().BeNull();
        harness.Credential.FailedAttempts.Should().Be(5);

        var pinLogin = await harness.UseCase.VerifyPinAsync(AuthHarness.InitialPin);
        pinLogin.Status.Should().Be(ParentAuthStatus.LockedOut);

        harness.Audits.Entries
            .Where(x => x.EventType == AuditEventType.AuthenticationFailed)
            .Should()
            .HaveCount(5)
            .And.OnlyContain(x => x.Description.Contains("recovery code"));
        harness.Audits.Entries.Should().ContainSingle(x => x.EventType == AuditEventType.LockoutTriggered);
    }

    [Fact]
    public async Task A_rotated_recovery_code_cannot_be_reused()
    {
        var harness = await AuthHarness.WithCredentialAsync();
        await harness.UseCase.ResetPinWithRecoveryCodeAsync(
            AuthHarness.FirstRecoveryCode,
            AuthHarness.ReplacementPin);

        var stale = await harness.UseCase.ResetPinWithRecoveryCodeAsync(
            AuthHarness.FirstRecoveryCode,
            AuthHarness.SecondReplacementPin);

        stale.Status.Should().Be(ParentAuthStatus.InvalidCredential);
        stale.RecoveryCode.Should().BeNull();
        harness.Credential.FailedAttempts.Should().Be(1);

        var current = await harness.UseCase.ResetPinWithRecoveryCodeAsync(
            AuthHarness.SecondRecoveryCode,
            AuthHarness.SecondReplacementPin);

        current.Status.Should().Be(ParentAuthStatus.Success);
        current.RecoveryCode.Should().Be(AuthHarness.ThirdRecoveryCode);
    }

    [Fact]
    public async Task Reset_rejects_a_weak_new_pin_before_checking_the_recovery_code()
    {
        var harness = await AuthHarness.WithCredentialAsync();
        var updateCount = harness.Credentials.UpdateCount;

        var act = () => harness.UseCase.ResetPinWithRecoveryCodeAsync(
            AuthHarness.FirstRecoveryCode,
            "123456");

        await act.Should().ThrowAsync<ArgumentException>();
        harness.Credential.FailedAttempts.Should().Be(0);
        harness.Credentials.UpdateCount.Should().Be(updateCount);
        harness.RecoveryCodes.GeneratedCount.Should().Be(1);
    }

    [Fact]
    public async Task RegenerateRecoveryCode_returns_a_new_code_and_invalidates_the_old_one()
    {
        var harness = await AuthHarness.WithCredentialAsync();

        var result = await harness.UseCase.RegenerateRecoveryCodeAsync(AuthHarness.InitialPin);

        result.Status.Should().Be(ParentAuthStatus.Success);
        result.RecoveryCode.Should().Be(AuthHarness.SecondRecoveryCode);
        harness.Hasher.Verify(
                AuthHarness.SecondRecoveryCode,
                new SecretHash(
                    harness.Credential.RecoveryCodeHash,
                    harness.Credential.RecoveryCodeSalt,
                    harness.Credential.RecoveryCodeIterations))
            .Should()
            .BeTrue();
        harness.Hasher.Verify(
                AuthHarness.FirstRecoveryCode,
                new SecretHash(
                    harness.Credential.RecoveryCodeHash,
                    harness.Credential.RecoveryCodeSalt,
                    harness.Credential.RecoveryCodeIterations))
            .Should()
            .BeFalse();
        harness.Audits.Entries.Should()
            .ContainSingle(x => x.EventType == AuditEventType.RecoveryCodeRegenerated);
        harness.Audits.Entries.Select(x => $"{x.Description} {x.MetadataJson}")
            .Should().NotContain(text => text!.Contains(AuthHarness.SecondRecoveryCode));
    }

    [Fact]
    public async Task RegenerateRecoveryCode_with_a_wrong_pin_uses_the_shared_lockout()
    {
        var harness = await AuthHarness.WithCredentialAsync();
        var previousHash = harness.Credential.RecoveryCodeHash;

        var result = await harness.UseCase.RegenerateRecoveryCodeAsync("482916");

        result.Status.Should().Be(ParentAuthStatus.InvalidCredential);
        result.RemainingAttempts.Should().Be(4);
        result.RecoveryCode.Should().BeNull();
        harness.Credential.FailedAttempts.Should().Be(1);
        harness.Credential.RecoveryCodeHash.Should().Be(previousHash);
        harness.Audits.Entries.Should().ContainSingle(x => x.EventType == AuditEventType.AuthenticationFailed);
    }

    [Fact]
    public async Task RegenerateRecoveryCode_reports_not_configured_without_a_credential()
    {
        var harness = new AuthHarness();

        var result = await harness.UseCase.RegenerateRecoveryCodeAsync(AuthHarness.InitialPin);

        result.Status.Should().Be(ParentAuthStatus.NotConfigured);
        result.RecoveryCode.Should().BeNull();
        harness.Audits.Entries.Should().BeEmpty();
    }

    [Fact]
    public async Task Audit_entries_never_contain_secrets()
    {
        var harness = await AuthHarness.WithCredentialAsync();
        await harness.UseCase.VerifyPinAsync("482916");
        await harness.UseCase.ResetPinWithRecoveryCodeAsync(
            "ZZZZZZZZZZZZ",
            AuthHarness.ReplacementPin);

        var auditText = string.Join(
            "\n",
            harness.Audits.Entries.Select(x => $"{x.Description} {x.MetadataJson}"));

        auditText.Should()
            .NotContain(AuthHarness.InitialPin)
            .And.NotContain("482916")
            .And.NotContain(AuthHarness.FirstRecoveryCode)
            .And.NotContain("ZZZZZZZZZZZZ");
    }
}
