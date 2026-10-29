using EduFocus.Domain.Entities;
using EduFocus.Domain.Enums;
using FluentAssertions;

namespace EduFocus.Domain.Tests.Domain;

public class AppSettingsTests
{
    private static readonly DateTime Now = new(2026, 10, 29, 7, 0, 0, DateTimeKind.Utc);

    [Fact]
    public void CreateDefault_UsesSchemaDefaults()
    {
        var settings = AppSettings.CreateDefault(Now);

        settings.ProtectionEnabled.Should().BeFalse();
        settings.ProtectionSource.Should().Be(ProtectionSource.None);
        settings.MonitoringEnabled.Should().BeTrue();
        settings.ScheduleEnabled.Should().BeFalse();
        settings.MonitorOnlyDuringClasses.Should().BeFalse();
        settings.DistractionThresholdSeconds.Should().Be(20);
        settings.AbsenceThresholdSeconds.Should().Be(30);
        settings.SampleIntervalSeconds.Should().Be(15);
        settings.ActivityRetentionDays.Should().Be(30);
        settings.EventRetentionDays.Should().Be(90);
        settings.SessionRetentionDays.Should().Be(365);
        settings.StartWithWindows.Should().BeTrue();
        settings.Theme.Should().Be(AppTheme.Dark);
        settings.Language.Should().Be(AppLanguage.Vi);
        settings.OnboardingCompleted.Should().BeFalse();
        settings.HasPrivacyConsent.Should().BeFalse();
        settings.PrivacyAcceptedAtUtc.Should().BeNull();
        settings.GuardianAcknowledgedAtUtc.Should().BeNull();
        settings.ScheduleOverrideUntilUtc.Should().BeNull();
        settings.LastCleanShutdownAtUtc.Should().BeNull();
        settings.CreatedAtUtc.Should().Be(Now);
        settings.UpdatedAtUtc.Should().Be(Now);
    }

    [Fact]
    public void ProtectionState_RoundTrips()
    {
        var settings = AppSettings.CreateDefault(Now);

        settings.EnableProtection(Now.AddMinutes(1));
        settings.ProtectionEnabled.Should().BeTrue();
        settings.ProtectionSource.Should().Be(ProtectionSource.Manual);

        settings.DisableProtection(Now.AddMinutes(2));
        settings.ProtectionEnabled.Should().BeFalse();
        settings.ProtectionSource.Should().Be(ProtectionSource.None);
        settings.UpdatedAtUtc.Should().Be(Now.AddMinutes(2));
    }

    [Fact]
    public void Schedule_enable_records_schedule_ownership()
    {
        var settings = AppSettings.CreateDefault(Now);

        settings.EnableProtection(Now.AddMinutes(1), ProtectionSource.Schedule);

        settings.ProtectionEnabled.Should().BeTrue();
        settings.ProtectionSource.Should().Be(ProtectionSource.Schedule);
    }

    [Fact]
    public void Manual_enable_promotes_schedule_ownership_and_clears_override()
    {
        var settings = AppSettings.CreateDefault(Now);
        settings.EnableProtection(Now.AddMinutes(1), ProtectionSource.Schedule);
        settings.SetScheduleOverrideUntil(Now.AddHours(2), Now.AddMinutes(2));

        settings.EnableProtection(Now.AddMinutes(3));

        settings.ProtectionSource.Should().Be(ProtectionSource.Manual);
        settings.ScheduleOverrideUntilUtc.Should().BeNull();
    }

    [Fact]
    public void Enable_rejects_missing_or_unknown_source()
    {
        var settings = AppSettings.CreateDefault(Now);

        var none = () => settings.EnableProtection(Now, ProtectionSource.None);
        var unknown = () => settings.EnableProtection(Now, (ProtectionSource)99);

        none.Should().Throw<ArgumentOutOfRangeException>().WithParameterName("source");
        unknown.Should().Throw<ArgumentOutOfRangeException>().WithParameterName("source");
    }

    [Fact]
    public void Disable_with_schedule_override_keeps_protection_off_until_window_end()
    {
        var settings = AppSettings.CreateDefault(Now);
        settings.EnableProtection(Now.AddMinutes(1), ProtectionSource.Schedule);

        settings.DisableProtectionWithScheduleOverride(Now.AddHours(1), Now.AddMinutes(2));

        settings.ProtectionEnabled.Should().BeFalse();
        settings.ProtectionSource.Should().Be(ProtectionSource.None);
        settings.ScheduleOverrideUntilUtc.Should().Be(Now.AddHours(1));
        settings.IsScheduleOverridden(Now.AddMinutes(30)).Should().BeTrue();
        settings.IsScheduleOverridden(Now.AddHours(1)).Should().BeFalse();
    }

    [Fact]
    public void Disable_with_schedule_override_requires_a_future_end()
    {
        var settings = AppSettings.CreateDefault(Now);

        var act = () => settings.DisableProtectionWithScheduleOverride(Now, Now);

        act.Should().Throw<ArgumentException>().WithParameterName("untilUtc");
    }

    [Fact]
    public void EnableProtection_ClearsTemporaryScheduleOverride()
    {
        var settings = AppSettings.CreateDefault(Now);
        settings.SetScheduleOverrideUntil(Now.AddHours(2), Now.AddMinutes(1));

        settings.EnableProtection(Now.AddMinutes(2));

        settings.ScheduleOverrideUntilUtc.Should().BeNull();
    }

    [Fact]
    public void DisableProtection_ClearsTemporaryScheduleOverride()
    {
        var settings = AppSettings.CreateDefault(Now);
        settings.EnableProtection(Now.AddMinutes(1));
        settings.SetScheduleOverrideUntil(Now.AddHours(2), Now.AddMinutes(2));

        settings.DisableProtection(Now.AddMinutes(3));

        settings.ProtectionEnabled.Should().BeFalse();
        settings.ScheduleOverrideUntilUtc.Should().BeNull();
    }

    [Fact]
    public void ScheduleOverride_IsActiveOnlyBeforeExpiry()
    {
        var settings = AppSettings.CreateDefault(Now);
        settings.SetScheduleOverrideUntil(Now.AddHours(2), Now.AddMinutes(1));

        settings.IsScheduleOverridden(Now.AddHours(1)).Should().BeTrue();
        settings.IsScheduleOverridden(Now.AddHours(2)).Should().BeFalse();
        settings.IsScheduleOverridden(Now.AddHours(3)).Should().BeFalse();
    }

    [Fact]
    public void ScheduleOverride_MustEndInTheFuture()
    {
        var settings = AppSettings.CreateDefault(Now);

        var act = () => settings.SetScheduleOverrideUntil(Now, Now);

        act.Should().Throw<ArgumentException>();
    }

    [Fact]
    public void DisablingSchedule_ClearsOverride()
    {
        var settings = AppSettings.CreateDefault(Now);
        settings.SetScheduleOverrideUntil(Now.AddHours(2), Now.AddMinutes(1));

        settings.SetScheduleEnabled(false, Now.AddMinutes(2));

        settings.ScheduleOverrideUntilUtc.Should().BeNull();
    }

    [Fact]
    public void UpdateRetention_RejectsNonPositiveValues()
    {
        var settings = AppSettings.CreateDefault(Now);

        var act = () => settings.UpdateRetention(0, 90, 365, Now);

        act.Should().Throw<ArgumentOutOfRangeException>().WithParameterName("activityDays");
    }

    [Fact]
    public void UpdateThresholds_RejectsNonPositiveValues()
    {
        var settings = AppSettings.CreateDefault(Now);

        var act = () => settings.UpdateThresholds(20, 30, 0, Now);

        act.Should().Throw<ArgumentOutOfRangeException>().WithParameterName("sampleIntervalSeconds");
    }

    [Fact]
    public void CameraDevice_IsTrimmedAndBlankBecomesNull()
    {
        var settings = AppSettings.CreateDefault(Now);

        settings.SetCameraDevice("  cam-1  ", Now.AddMinutes(1));
        settings.CameraDeviceId.Should().Be("cam-1");

        settings.SetCameraDevice("   ", Now.AddMinutes(2));
        settings.CameraDeviceId.Should().BeNull();
    }

    [Fact]
    public void UpdateAppearance_RejectsUndefinedEnum()
    {
        var settings = AppSettings.CreateDefault(Now);

        var act = () => settings.UpdateAppearance((AppTheme)99, AppLanguage.Vi, Now);

        act.Should().Throw<ArgumentOutOfRangeException>();
    }

    [Fact]
    public void Timestamps_MustBeUtcAndMonotonic()
    {
        var local = new DateTime(2026, 10, 29, 7, 0, 0, DateTimeKind.Unspecified);
        var act = () => AppSettings.CreateDefault(local);
        act.Should().Throw<ArgumentException>();

        var settings = AppSettings.CreateDefault(Now);
        settings.EnableProtection(Now.AddMinutes(10));
        var backwards = () => settings.DisableProtection(Now.AddMinutes(5));
        backwards.Should().Throw<ArgumentException>();
    }

    [Fact]
    public void OnboardingAndCleanShutdown_AreRecorded()
    {
        var settings = AppSettings.CreateDefault(Now);

        settings.RecordPrivacyConsent(Now.AddSeconds(30));
        settings.CompleteOnboarding(Now.AddMinutes(1));
        settings.RecordCleanShutdown(Now.AddMinutes(2));

        settings.OnboardingCompleted.Should().BeTrue();
        settings.HasPrivacyConsent.Should().BeTrue();
        settings.PrivacyAcceptedAtUtc.Should().Be(Now.AddSeconds(30));
        settings.GuardianAcknowledgedAtUtc.Should().Be(Now.AddSeconds(30));
        settings.LastCleanShutdownAtUtc.Should().Be(Now.AddMinutes(2));

        settings.ClearCleanShutdown(Now.AddMinutes(3));
        settings.LastCleanShutdownAtUtc.Should().BeNull();
    }

    [Fact]
    public void Onboarding_cannot_complete_without_privacy_consent()
    {
        var settings = AppSettings.CreateDefault(Now);

        var act = () => settings.CompleteOnboarding(Now.AddMinutes(1));

        act.Should().Throw<InvalidOperationException>();
        settings.OnboardingCompleted.Should().BeFalse();
    }

    [Fact]
    public void Recording_privacy_consent_keeps_the_first_timestamp()
    {
        var settings = AppSettings.CreateDefault(Now);

        settings.RecordPrivacyConsent(Now.AddMinutes(1));
        settings.RecordPrivacyConsent(Now.AddMinutes(5));

        settings.PrivacyAcceptedAtUtc.Should().Be(Now.AddMinutes(1));
        settings.GuardianAcknowledgedAtUtc.Should().Be(Now.AddMinutes(1));
    }
}
