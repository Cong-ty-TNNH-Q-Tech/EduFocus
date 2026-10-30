using EduFocus.Domain.Enums;
using EduFocus.Ports.DTOs;
using EduFocus.UseCases.Tests.TestSupport;
using FluentAssertions;

namespace EduFocus.UseCases.Tests.Onboarding;

public sealed class OnboardingServiceTests
{
    [Fact]
    public async Task GetState_returns_safe_defaults_before_anything_is_saved()
    {
        var harness = new OnboardingHarness();

        var state = await harness.Service.GetStateAsync();

        state.PrivacyConsentRecorded.Should().BeFalse();
        state.CredentialConfigured.Should().BeFalse();
        state.OnboardingCompleted.Should().BeFalse();
        state.Preferences.MonitoringEnabled.Should().BeTrue();
        state.Preferences.StartWithWindows.Should().BeTrue();
        harness.Settings.Stored.Should().BeNull();
    }

    [Fact]
    public async Task Recording_consent_persists_both_confirmations_and_audits_once()
    {
        var harness = new OnboardingHarness();

        await harness.Service.RecordPrivacyConsentAsync();
        await harness.Service.RecordPrivacyConsentAsync();

        harness.Settings.AddCount.Should().Be(1);
        harness.Settings.UpdateCount.Should().Be(1);
        var settings = harness.Settings.Stored!;
        settings.HasPrivacyConsent.Should().BeTrue();
        settings.PrivacyAcceptedAtUtc.Should().Be(AuthHarness.Start.UtcDateTime);
        settings.GuardianAcknowledgedAtUtc.Should().Be(AuthHarness.Start.UtcDateTime);
        harness.Auth.Audits.Entries.Should()
            .ContainSingle(x => x.EventType == AuditEventType.PrivacyConsentRecorded);
    }

    [Fact]
    public async Task Pin_setup_is_rejected_until_consent_is_recorded()
    {
        var harness = new OnboardingHarness();

        var act = () => harness.Service.ConfigurePinAsync(AuthHarness.InitialPin);

        await act.Should().ThrowAsync<InvalidOperationException>();
        harness.Auth.Credentials.AddCount.Should().Be(0);
        harness.Auth.Audits.Entries.Should().BeEmpty();
    }

    [Fact]
    public async Task Successful_onboarding_persists_consent_preferences_pin_and_completion()
    {
        var harness = new OnboardingHarness();

        await harness.Service.RecordPrivacyConsentAsync();
        await harness.Service.SavePreferencesAsync(new OnboardingPreferences(
            MonitoringEnabled: false,
            StartWithWindows: false));
        var setup = await harness.Service.ConfigurePinAsync(AuthHarness.InitialPin);
        await harness.Service.CompleteAsync();

        setup.RecoveryCode.Should().Be(AuthHarness.FirstRecoveryCode);
        var state = await harness.Service.GetStateAsync();
        state.PrivacyConsentRecorded.Should().BeTrue();
        state.CredentialConfigured.Should().BeTrue();
        state.OnboardingCompleted.Should().BeTrue();
        state.Preferences.MonitoringEnabled.Should().BeFalse();
        state.Preferences.StartWithWindows.Should().BeFalse();

        var settings = harness.Settings.Stored!;
        settings.MonitoringEnabled.Should().BeFalse();
        settings.StartWithWindows.Should().BeFalse();
        settings.OnboardingCompleted.Should().BeTrue();
        harness.Settings.AddCount.Should().Be(1);
        harness.Auth.Audits.Entries.Should().Contain(x => x.EventType == AuditEventType.SettingsChanged);
        harness.Auth.Audits.Entries.Should().Contain(x => x.EventType == AuditEventType.OnboardingCompleted);
    }

    [Fact]
    public async Task Onboarding_cannot_complete_without_a_pin()
    {
        var harness = new OnboardingHarness();
        await harness.Service.RecordPrivacyConsentAsync();
        await harness.Service.SavePreferencesAsync(new OnboardingPreferences(true, true));

        var act = () => harness.Service.CompleteAsync();

        await act.Should().ThrowAsync<InvalidOperationException>();
        (await harness.Service.GetStateAsync()).OnboardingCompleted.Should().BeFalse();
        harness.Auth.Audits.Entries.Should()
            .NotContain(x => x.EventType == AuditEventType.OnboardingCompleted);
    }

    [Fact]
    public async Task Cancelling_before_the_pin_keeps_completed_steps_for_the_next_launch()
    {
        var harness = new OnboardingHarness();
        await harness.Service.RecordPrivacyConsentAsync();
        await harness.Service.SavePreferencesAsync(new OnboardingPreferences(false, true));

        var restarted = harness.CreateService();
        var state = await restarted.GetStateAsync();

        state.PrivacyConsentRecorded.Should().BeTrue();
        state.CredentialConfigured.Should().BeFalse();
        state.OnboardingCompleted.Should().BeFalse();
        state.Preferences.MonitoringEnabled.Should().BeFalse();
        state.Preferences.StartWithWindows.Should().BeTrue();
    }

    [Fact]
    public async Task Restart_after_pin_setup_reissues_a_recovery_code_and_then_completes()
    {
        var harness = new OnboardingHarness();
        await harness.Service.RecordPrivacyConsentAsync();
        await harness.Service.SavePreferencesAsync(new OnboardingPreferences(true, true));
        await harness.Service.ConfigurePinAsync(AuthHarness.InitialPin);

        var restarted = harness.CreateService();
        var state = await restarted.GetStateAsync();
        state.CredentialConfigured.Should().BeTrue();
        state.OnboardingCompleted.Should().BeFalse();

        var reissued = await restarted.ReissueRecoveryCodeAsync(AuthHarness.InitialPin);

        reissued.Status.Should().Be(ParentAuthStatus.Success);
        reissued.RecoveryCode.Should().Be(AuthHarness.SecondRecoveryCode);
        harness.Auth.Audits.Entries.Should()
            .ContainSingle(x => x.EventType == AuditEventType.RecoveryCodeRegenerated);

        await restarted.CompleteAsync();

        (await restarted.GetStateAsync()).OnboardingCompleted.Should().BeTrue();
        var auditText = string.Join(
            "\n",
            harness.Auth.Audits.Entries.Select(x => $"{x.Description} {x.MetadataJson}"));
        auditText.Should()
            .NotContain(AuthHarness.InitialPin)
            .And.NotContain(AuthHarness.FirstRecoveryCode)
            .And.NotContain(AuthHarness.SecondRecoveryCode);
    }

    [Fact]
    public async Task Reissuing_a_recovery_code_with_a_wrong_pin_counts_as_a_failure()
    {
        var harness = new OnboardingHarness();
        await harness.Service.RecordPrivacyConsentAsync();
        await harness.Service.ConfigurePinAsync(AuthHarness.InitialPin);

        var result = await harness.Service.ReissueRecoveryCodeAsync("482916");

        result.Status.Should().Be(ParentAuthStatus.InvalidCredential);
        result.RemainingAttempts.Should().Be(4);
        result.RecoveryCode.Should().BeNull();
        harness.Auth.Credential.FailedAttempts.Should().Be(1);
    }

    [Fact]
    public async Task Completing_twice_is_idempotent()
    {
        var harness = new OnboardingHarness();
        await harness.Service.RecordPrivacyConsentAsync();
        await harness.Service.ConfigurePinAsync(AuthHarness.InitialPin);
        await harness.Service.CompleteAsync();
        var updateCount = harness.Settings.UpdateCount;

        await harness.Service.CompleteAsync();

        harness.Settings.UpdateCount.Should().Be(updateCount);
        harness.Auth.Audits.Entries
            .Count(x => x.EventType == AuditEventType.OnboardingCompleted)
            .Should()
            .Be(1);
    }

    [Fact]
    public async Task Restart_after_pin_setup_can_reset_pin_with_recovery_code()
    {
        var harness = new OnboardingHarness();
        await harness.Service.RecordPrivacyConsentAsync();
        var setup = await harness.Service.ConfigurePinAsync(AuthHarness.InitialPin);

        var restarted = harness.CreateService();
        var reset = await restarted.ResetPinWithRecoveryCodeAsync(setup.RecoveryCode, AuthHarness.UpdatedPin);

        reset.Status.Should().Be(ParentAuthStatus.Success);
        reset.RecoveryCode.Should().Be(AuthHarness.SecondRecoveryCode);
    }
}
