using EduFocus.Domain.Common;
using EduFocus.Domain.Enums;
using EduFocus.Domain.Privacy;
using EduFocus.Domain.ValueObjects;

namespace EduFocus.Domain.Entities;

/// <summary>
/// Singleton application settings. Protection is the source of truth for ON/OFF
/// and is restored after a crash or Windows shutdown (BR-17, BR-30).
/// </summary>
public sealed class AppSettings
{
    public const int MaxCameraDeviceIdLength = 256;

    private AppSettings(AppSettingsId id, DateTime nowUtc)
    {
        Id = id;
        ProtectionEnabled = false;
        MonitoringEnabled = true;
        ScheduleEnabled = false;
        MonitorOnlyDuringClasses = false;
        CameraDeviceId = null;
        DistractionThresholdSeconds = 20;
        AbsenceThresholdSeconds = 30;
        SampleIntervalSeconds = 15;
        ActivityRetentionDays = 30;
        EventRetentionDays = 90;
        SessionRetentionDays = 365;
        StartWithWindows = true;
        Theme = AppTheme.Dark;
        Language = AppLanguage.Vi;
        OnboardingCompleted = false;
        ScheduleOverrideUntilUtc = null;
        LastCleanShutdownAtUtc = null;
        CreatedAtUtc = nowUtc;
        UpdatedAtUtc = nowUtc;
    }

    public AppSettingsId Id { get; }

    public bool ProtectionEnabled { get; private set; }

    public bool MonitoringEnabled { get; private set; }

    public bool ScheduleEnabled { get; private set; }

    public bool MonitorOnlyDuringClasses { get; private set; }

    public string? CameraDeviceId { get; private set; }

    public int DistractionThresholdSeconds { get; private set; }

    public int AbsenceThresholdSeconds { get; private set; }

    public int SampleIntervalSeconds { get; private set; }

    public int ActivityRetentionDays { get; private set; }

    public int EventRetentionDays { get; private set; }

    public int SessionRetentionDays { get; private set; }

    public bool StartWithWindows { get; private set; }

    public AppTheme Theme { get; private set; }

    public AppLanguage Language { get; private set; }

    public bool OnboardingCompleted { get; private set; }

    public DateTime? ScheduleOverrideUntilUtc { get; private set; }

    /// <summary>Set only by a valid PIN exit; Windows shutdown leaves it untouched (BR-30).</summary>
    public DateTime? LastCleanShutdownAtUtc { get; private set; }

    public DateTime CreatedAtUtc { get; }

    public DateTime UpdatedAtUtc { get; private set; }

    public static AppSettings CreateDefault(DateTime nowUtc)
    {
        Guard.Utc(nowUtc, nameof(nowUtc));
        return new AppSettings(AppSettingsId.New(), nowUtc);
    }

    /// <summary>Manual protection on clears any temporary schedule override (BR-07).</summary>
    public void EnableProtection(DateTime nowUtc)
    {
        ProtectionEnabled = true;
        ScheduleOverrideUntilUtc = null;
        Touch(nowUtc);
    }

    public void DisableProtection(DateTime nowUtc)
    {
        ProtectionEnabled = false;
        ScheduleOverrideUntilUtc = null;
        Touch(nowUtc);
    }

    public void SetScheduleOverrideUntil(DateTime untilUtc, DateTime nowUtc)
    {
        Guard.Utc(nowUtc, nameof(nowUtc));
        Guard.Utc(untilUtc, nameof(untilUtc));

        if (untilUtc <= nowUtc)
            throw new ArgumentException("Override must end in the future.", nameof(untilUtc));

        ScheduleOverrideUntilUtc = untilUtc;
        Touch(nowUtc);
    }

    public void ClearScheduleOverride(DateTime nowUtc)
    {
        ScheduleOverrideUntilUtc = null;
        Touch(nowUtc);
    }

    public bool IsScheduleOverridden(DateTime nowUtc)
    {
        Guard.Utc(nowUtc, nameof(nowUtc));
        return ScheduleOverrideUntilUtc is { } until && until > nowUtc;
    }

    public void SetScheduleEnabled(bool isEnabled, DateTime nowUtc)
    {
        ScheduleEnabled = isEnabled;

        if (!isEnabled)
            ScheduleOverrideUntilUtc = null;

        Touch(nowUtc);
    }

    public void UpdateMonitoring(bool monitoringEnabled, bool monitorOnlyDuringClasses, DateTime nowUtc)
    {
        MonitoringEnabled = monitoringEnabled;
        MonitorOnlyDuringClasses = monitorOnlyDuringClasses;
        Touch(nowUtc);
    }

    public void SetCameraDevice(string? cameraDeviceId, DateTime nowUtc)
    {
        Guard.MaxLength(cameraDeviceId, MaxCameraDeviceIdLength, nameof(cameraDeviceId));
        CameraDeviceId = string.IsNullOrWhiteSpace(cameraDeviceId) ? null : cameraDeviceId.Trim();
        Touch(nowUtc);
    }

    public void UpdateThresholds(
        int distractionThresholdSeconds,
        int absenceThresholdSeconds,
        int sampleIntervalSeconds,
        DateTime nowUtc)
    {
        Guard.InRange(distractionThresholdSeconds, 1, 3600, nameof(distractionThresholdSeconds));
        Guard.InRange(absenceThresholdSeconds, 1, 3600, nameof(absenceThresholdSeconds));
        Guard.InRange(sampleIntervalSeconds, 1, 3600, nameof(sampleIntervalSeconds));

        DistractionThresholdSeconds = distractionThresholdSeconds;
        AbsenceThresholdSeconds = absenceThresholdSeconds;
        SampleIntervalSeconds = sampleIntervalSeconds;
        Touch(nowUtc);
    }

    public void UpdateRetention(int activityDays, int eventDays, int sessionDays, DateTime nowUtc)
    {
        Guard.InRange(activityDays, 1, 3650, nameof(activityDays));
        Guard.InRange(eventDays, 1, 3650, nameof(eventDays));
        Guard.InRange(sessionDays, 1, 3650, nameof(sessionDays));

        ActivityRetentionDays = activityDays;
        EventRetentionDays = eventDays;
        SessionRetentionDays = sessionDays;
        Touch(nowUtc);
    }

    public void UpdateStartWithWindows(bool startWithWindows, DateTime nowUtc)
    {
        StartWithWindows = startWithWindows;
        Touch(nowUtc);
    }

    public void UpdateAppearance(AppTheme theme, AppLanguage language, DateTime nowUtc)
    {
        Guard.Defined(theme, nameof(theme));
        Guard.Defined(language, nameof(language));

        Theme = theme;
        Language = language;
        Touch(nowUtc);
    }

    public void CompleteOnboarding(DateTime nowUtc)
    {
        OnboardingCompleted = true;
        Touch(nowUtc);
    }

    public void RecordCleanShutdown(DateTime nowUtc)
    {
        LastCleanShutdownAtUtc = nowUtc;
        Touch(nowUtc);
    }

    public void ClearCleanShutdown(DateTime nowUtc)
    {
        LastCleanShutdownAtUtc = null;
        Touch(nowUtc);
    }

    private void Touch(DateTime nowUtc)
    {
        Guard.Utc(nowUtc, nameof(nowUtc));

        if (nowUtc < UpdatedAtUtc)
            throw new ArgumentException("Timestamp must not move backwards.", nameof(nowUtc));

        UpdatedAtUtc = nowUtc;
    }
}
