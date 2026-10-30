using EduFocus.Domain.Enums;

namespace EduFocus.Ports.DTOs;

/// <summary>
/// Editable privacy and monitoring preferences (UC11, issue #38). These values
/// never include PINs, recovery codes, clipboard content or camera frames.
/// </summary>
public sealed record PrivacySettings(
    bool MonitoringEnabled,
    bool MonitorOnlyDuringClasses,
    int DistractionThresholdSeconds,
    int AbsenceThresholdSeconds,
    int SampleIntervalSeconds,
    string? CameraDeviceId,
    int ActivityRetentionDays,
    int EventRetentionDays,
    int SessionRetentionDays,
    bool StartWithWindows,
    bool UpdateCheckEnabled,
    AppTheme Theme,
    AppLanguage Language);

/// <summary>Row counts shown in the privacy screen; never contains row content.</summary>
public sealed record PrivacyDataCounts(
    int Sessions,
    int ActiveSessions,
    int ActivitySamples,
    int AttentionEvents,
    int BlockedAttempts);

/// <summary>Settings plus storage information displayed to the parent.</summary>
public sealed record PrivacySnapshot(
    PrivacySettings Settings,
    PrivacyDataCounts Counts,
    long DatabaseSizeBytes);

/// <summary>Outcome of saving privacy settings; unchanged values write no audit entry.</summary>
public sealed record PrivacySettingsSaveResult(PrivacySettings Settings, bool Changed);

/// <summary>
/// Irreversible delete-all request. The confirmation must be typed exactly and
/// the PIN is re-verified even inside an authenticated Parent Mode session.
/// </summary>
public sealed record DataDeletionRequest(string Pin, string ConfirmationText);

public enum DataDeletionStatus
{
    Success,
    Incomplete,
    ConfirmationMismatch,
    InvalidPin,
    LockedOut,
    NotConfigured
}

/// <summary>Rows removed by a delete-all run. Audit entries and credentials are never deleted.</summary>
public sealed record DataDeletionCounts(
    int Sessions,
    int ActivitySamples,
    int AttentionEvents,
    int BlockedAttempts)
{
    public static DataDeletionCounts Empty { get; } = new(0, 0, 0, 0);
}

/// <summary>
/// Result of a delete-all run. Failures are table names only and never contain
/// row content; an incomplete run is never reported as success.
/// </summary>
public sealed record DataDeletionResult(
    DataDeletionStatus Status,
    DataDeletionCounts Deleted,
    IReadOnlyList<string> Failures,
    int RemainingAttempts,
    DateTime? LockoutUntilUtc);
