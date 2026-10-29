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

        settings.DisableProtection(Now.AddMinutes(2));
        settings.ProtectionEnabled.Should().BeFalse();
        settings.UpdatedAtUtc.Should().Be(Now.AddMinutes(2));
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

        settings.CompleteOnboarding(Now.AddMinutes(1));
        settings.RecordCleanShutdown(Now.AddMinutes(2));

        settings.OnboardingCompleted.Should().BeTrue();
        settings.LastCleanShutdownAtUtc.Should().Be(Now.AddMinutes(2));

        settings.ClearCleanShutdown(Now.AddMinutes(3));
        settings.LastCleanShutdownAtUtc.Should().BeNull();
    }
}
