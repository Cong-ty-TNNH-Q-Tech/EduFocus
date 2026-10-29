using System.Text.Json;
using EduFocus.Domain.Entities;
using EduFocus.Domain.Enums;
using EduFocus.Ports.DTOs;
using EduFocus.Ports.Inbound;
using EduFocus.Ports.Outbound.Persistence;
using EduFocus.Ports.Outbound.Services;

namespace EduFocus.UseCases.Privacy;

/// <summary>
/// Data and privacy settings from UC11 (issue #38). The parent must hold an
/// authenticated session; every effective change is persisted once and audited
/// without values that could identify the student.
/// </summary>
public sealed class PrivacySettingsService : IPrivacySettingsUseCase
{
    private readonly IAppSettingsRepository _settings;
    private readonly IPrivacyDataRepository _data;
    private readonly IParentAuthorizer _authorizer;
    private readonly IAuditLogRepository _auditLogs;
    private readonly ICorrelationContext _correlation;
    private readonly TimeProvider _timeProvider;

    public PrivacySettingsService(
        IAppSettingsRepository settings,
        IPrivacyDataRepository data,
        IParentAuthorizer authorizer,
        IAuditLogRepository auditLogs,
        ICorrelationContext correlation,
        TimeProvider timeProvider)
    {
        _settings = settings ?? throw new ArgumentNullException(nameof(settings));
        _data = data ?? throw new ArgumentNullException(nameof(data));
        _authorizer = authorizer ?? throw new ArgumentNullException(nameof(authorizer));
        _auditLogs = auditLogs ?? throw new ArgumentNullException(nameof(auditLogs));
        _correlation = correlation ?? throw new ArgumentNullException(nameof(correlation));
        _timeProvider = timeProvider ?? throw new ArgumentNullException(nameof(timeProvider));
    }

    public async Task<PrivacySnapshot> GetSnapshotAsync(CancellationToken cancellationToken = default)
    {
        _authorizer.EnsureAuthorized("Read privacy settings");

        var settings = await GetSettingsAsync(cancellationToken).ConfigureAwait(false);
        var counts = await _data.GetCountsAsync(cancellationToken).ConfigureAwait(false);
        var databaseSize = await _data.GetDatabaseSizeBytesAsync(cancellationToken).ConfigureAwait(false);

        return new PrivacySnapshot(ToDto(settings), counts, databaseSize);
    }

    public async Task<PrivacySettingsSaveResult> SaveAsync(
        PrivacySettings settings,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(settings);
        _authorizer.EnsureAuthorized("Update privacy settings");

        var now = UtcNow();
        var current = await GetSettingsAsync(cancellationToken).ConfigureAwait(false);
        var before = ToDto(current);

        current.UpdateMonitoring(settings.MonitoringEnabled, settings.MonitorOnlyDuringClasses, now);
        current.UpdateThresholds(
            settings.DistractionThresholdSeconds,
            settings.AbsenceThresholdSeconds,
            settings.SampleIntervalSeconds,
            now);
        current.SetCameraDevice(settings.CameraDeviceId, now);
        current.UpdateRetention(
            settings.ActivityRetentionDays,
            settings.EventRetentionDays,
            settings.SessionRetentionDays,
            now);
        current.UpdateStartWithWindows(settings.StartWithWindows, now);
        current.UpdateUpdateCheck(settings.UpdateCheckEnabled, now);
        current.UpdateAppearance(settings.Theme, settings.Language, now);

        var after = ToDto(current);
        var changedFields = DescribeChanges(before, after);

        if (changedFields.Count > 0)
        {
            await _settings.UpdateAsync(current, cancellationToken).ConfigureAwait(false);
            await WriteAuditAsync(now, changedFields, cancellationToken).ConfigureAwait(false);
        }

        return new PrivacySettingsSaveResult(after, changedFields.Count > 0);
    }

    private async Task<AppSettings> GetSettingsAsync(CancellationToken cancellationToken)
    {
        return await _settings.GetAsync(cancellationToken).ConfigureAwait(false)
            ?? throw new InvalidOperationException("Application settings are not initialized.");
    }

    private async Task WriteAuditAsync(
        DateTime now,
        IReadOnlyList<string> changedFields,
        CancellationToken cancellationToken)
    {
        var metadata = JsonSerializer.Serialize(new { changed = changedFields });

        await _auditLogs
            .AddAsync(
                AuditLog.Record(
                    AuditActor.Parent,
                    AuditEventType.SettingsChanged,
                    "Parent updated data and privacy settings.",
                    now,
                    metadata,
                    _correlation.Current),
                cancellationToken)
            .ConfigureAwait(false);
    }

    private static IReadOnlyList<string> DescribeChanges(PrivacySettings before, PrivacySettings after)
    {
        var changed = new List<string>();

        if (before.MonitoringEnabled != after.MonitoringEnabled ||
            before.MonitorOnlyDuringClasses != after.MonitorOnlyDuringClasses)
        {
            changed.Add("monitoring");
        }

        if (before.DistractionThresholdSeconds != after.DistractionThresholdSeconds ||
            before.AbsenceThresholdSeconds != after.AbsenceThresholdSeconds ||
            before.SampleIntervalSeconds != after.SampleIntervalSeconds)
        {
            changed.Add("thresholds");
        }

        if (!string.Equals(before.CameraDeviceId, after.CameraDeviceId, StringComparison.Ordinal))
            changed.Add("camera");

        if (before.ActivityRetentionDays != after.ActivityRetentionDays ||
            before.EventRetentionDays != after.EventRetentionDays ||
            before.SessionRetentionDays != after.SessionRetentionDays)
        {
            changed.Add("retention");
        }

        if (before.StartWithWindows != after.StartWithWindows)
            changed.Add("startWithWindows");

        if (before.UpdateCheckEnabled != after.UpdateCheckEnabled)
            changed.Add("updateCheck");

        if (before.Theme != after.Theme || before.Language != after.Language)
            changed.Add("appearance");

        return changed;
    }

    private static PrivacySettings ToDto(AppSettings settings) =>
        new(
            settings.MonitoringEnabled,
            settings.MonitorOnlyDuringClasses,
            settings.DistractionThresholdSeconds,
            settings.AbsenceThresholdSeconds,
            settings.SampleIntervalSeconds,
            settings.CameraDeviceId,
            settings.ActivityRetentionDays,
            settings.EventRetentionDays,
            settings.SessionRetentionDays,
            settings.StartWithWindows,
            settings.UpdateCheckEnabled,
            settings.Theme,
            settings.Language);

    private DateTime UtcNow() => _timeProvider.GetUtcNow().UtcDateTime;
}
