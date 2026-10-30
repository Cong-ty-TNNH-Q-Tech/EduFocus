using EduFocus.Domain.Entities;
using EduFocus.Domain.Enums;
using EduFocus.Domain.Scheduling;
using EduFocus.Domain.ValueObjects;
using EduFocus.Ports.DTOs;
using EduFocus.Ports.Inbound;
using EduFocus.Ports.Outbound.Persistence;
using EduFocus.Ports.Outbound.Services;
using EduFocus.Ports.Protection;

namespace EduFocus.UseCases.Schedule;

/// <summary>
/// Weekly protection schedule (UC06). Mutations require Parent Mode; the 30-second
/// evaluator uses the local wall clock, auto-enables inside windows, auto-disables
/// only schedule-owned protection at the end of a window, and respects a temporary
/// parent override. Wall-clock jumps against the monotonic clock are audited and
/// never cause an automatic disable in the affected tick.
/// </summary>
public sealed class ScheduleService : IScheduleUseCase
{
    private const string EngineEnableWarning =
        "Không thể tự bật bảo vệ theo lịch. Hãy kiểm tra quyền quản trị và thử lại.";

    private const string EngineDisableWarning =
        "Không thể tự tắt bảo vệ khi hết khung giờ. Trạng thái sẽ được thử lại ở lần đánh giá sau.";

    private static readonly TimeSpan MaxClockDrift = TimeSpan.FromMinutes(2);

    private readonly IScheduleWindowRepository _windows;
    private readonly IAppSettingsRepository _settings;
    private readonly IProtectionUseCase _protection;
    private readonly IParentAuthorizer _authorizer;
    private readonly IAuditLogRepository _auditLogs;
    private readonly ICorrelationContext _correlation;
    private readonly IAppLogger _logger;
    private readonly TimeProvider _timeProvider;
    private readonly IMonotonicClockPort _monotonic;
    private readonly SemaphoreSlim _gate = new(1, 1);

    private DateTime? _lastWallUtc;
    private TimeSpan? _lastTick;

    public ScheduleService(
        IScheduleWindowRepository windows,
        IAppSettingsRepository settings,
        IProtectionUseCase protection,
        IParentAuthorizer authorizer,
        IAuditLogRepository auditLogs,
        ICorrelationContext correlation,
        IAppLogger logger,
        TimeProvider timeProvider,
        IMonotonicClockPort monotonic)
    {
        _windows = windows ?? throw new ArgumentNullException(nameof(windows));
        _settings = settings ?? throw new ArgumentNullException(nameof(settings));
        _protection = protection ?? throw new ArgumentNullException(nameof(protection));
        _authorizer = authorizer ?? throw new ArgumentNullException(nameof(authorizer));
        _auditLogs = auditLogs ?? throw new ArgumentNullException(nameof(auditLogs));
        _correlation = correlation ?? throw new ArgumentNullException(nameof(correlation));
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
        _timeProvider = timeProvider ?? throw new ArgumentNullException(nameof(timeProvider));
        _monotonic = monotonic ?? throw new ArgumentNullException(nameof(monotonic));
    }

    public async Task<ScheduleOverview> GetOverviewAsync(CancellationToken cancellationToken = default)
    {
        var settings = await _settings.GetAsync(cancellationToken);
        var windows = await _windows.ListAsync(cancellationToken);

        return BuildOverview(settings, windows, UtcNow());
    }

    public async Task<ScheduleOperationResult> AddWindowAsync(
        ScheduleWindowDraft draft,
        CancellationToken cancellationToken = default)
    {
        await _gate.WaitAsync(cancellationToken);
        try
        {
            _authorizer.EnsureAuthorized("change the protection schedule");

            var now = UtcNow();
            var existing = await _windows.ListAsync(cancellationToken);
            var window = ScheduleWindow.Create(
                draft.Days,
                draft.StartMinutes,
                draft.EndMinutes,
                draft.Label,
                now,
                draft.IsEnabled);

            ScheduleValidator.EnsureCanAdd(existing, window);
            await _windows.AddAsync(window, cancellationToken);

            var settings = await RequireSettingsAsync(cancellationToken);
            if (!settings.ScheduleEnabled)
            {
                var settingsNow = now < settings.UpdatedAtUtc ? settings.UpdatedAtUtc : now;
                settings.SetScheduleEnabled(true, settingsNow);
                await _settings.UpdateAsync(settings, cancellationToken);
            }

            await WriteAuditAsync(
                AuditActor.Parent,
                AuditEventType.ScheduleChanged,
                $"Parent added schedule window {Describe(window)}.",
                now,
                cancellationToken);

            return await CompleteWithEvaluationAsync(cancellationToken);
        }
        finally
        {
            _gate.Release();
        }
    }

    public async Task<ScheduleOperationResult> UpdateWindowAsync(
        Guid id,
        ScheduleWindowDraft draft,
        CancellationToken cancellationToken = default)
    {
        await _gate.WaitAsync(cancellationToken);
        try
        {
            _authorizer.EnsureAuthorized("change the protection schedule");

            var now = UtcNow();
            var target = await RequireWindowAsync(id, cancellationToken);
            var existing = await _windows.ListAsync(cancellationToken);

            var candidate = ScheduleWindow.Create(
                draft.Days,
                draft.StartMinutes,
                draft.EndMinutes,
                draft.Label,
                now,
                draft.IsEnabled);

            ScheduleValidator.EnsureCanAdd(
                existing.Where(window => window.Id != target.Id),
                candidate);

            var targetNow = now < target.UpdatedAtUtc ? target.UpdatedAtUtc : now;
            target.Reschedule(draft.Days, draft.StartMinutes, draft.EndMinutes, draft.Label, targetNow);
            if (target.IsEnabled != draft.IsEnabled)
                target.SetEnabled(draft.IsEnabled, targetNow);

            await _windows.UpdateAsync(target, cancellationToken);

            await WriteAuditAsync(
                AuditActor.Parent,
                AuditEventType.ScheduleChanged,
                $"Parent updated schedule window {Describe(target)}.",
                now,
                cancellationToken);

            return await CompleteWithEvaluationAsync(cancellationToken);
        }
        finally
        {
            _gate.Release();
        }
    }

    public async Task<ScheduleOperationResult> SetWindowEnabledAsync(
        Guid id,
        bool isEnabled,
        CancellationToken cancellationToken = default)
    {
        await _gate.WaitAsync(cancellationToken);
        try
        {
            _authorizer.EnsureAuthorized("change the protection schedule");

            var now = UtcNow();
            var target = await RequireWindowAsync(id, cancellationToken);

            if (target.IsEnabled == isEnabled)
                return await CompleteWithEvaluationAsync(cancellationToken);

            if (isEnabled)
            {
                var others = await _windows.ListAsync(cancellationToken);
                foreach (var other in others)
                {
                    if (other.Id != target.Id && other.IsEnabled && target.Overlaps(other))
                    {
                        throw new ArgumentException(
                            "Window overlaps an enabled schedule window.",
                            nameof(isEnabled));
                    }
                }
            }

            var targetNow = now < target.UpdatedAtUtc ? target.UpdatedAtUtc : now;
            target.SetEnabled(isEnabled, targetNow);
            await _windows.UpdateAsync(target, cancellationToken);

            await WriteAuditAsync(
                AuditActor.Parent,
                AuditEventType.ScheduleChanged,
                $"Parent {(isEnabled ? "enabled" : "disabled")} schedule window {Describe(target)}.",
                now,
                cancellationToken);

            return await CompleteWithEvaluationAsync(cancellationToken);
        }
        finally
        {
            _gate.Release();
        }
    }

    public async Task<ScheduleOperationResult> DeleteWindowAsync(
        Guid id,
        CancellationToken cancellationToken = default)
    {
        await _gate.WaitAsync(cancellationToken);
        try
        {
            _authorizer.EnsureAuthorized("change the protection schedule");

            var now = UtcNow();
            var target = await RequireWindowAsync(id, cancellationToken);
            await _windows.DeleteAsync(target.Id, cancellationToken);

            await WriteAuditAsync(
                AuditActor.Parent,
                AuditEventType.ScheduleChanged,
                $"Parent deleted schedule window {Describe(target)}.",
                now,
                cancellationToken);

            return await CompleteWithEvaluationAsync(cancellationToken);
        }
        finally
        {
            _gate.Release();
        }
    }

    public async Task<ScheduleOperationResult> SetScheduleEnabledAsync(
        bool isEnabled,
        CancellationToken cancellationToken = default)
    {
        await _gate.WaitAsync(cancellationToken);
        try
        {
            _authorizer.EnsureAuthorized("change the protection schedule");

            var now = UtcNow();
            var settings = await RequireSettingsAsync(cancellationToken);

            if (settings.ScheduleEnabled == isEnabled)
                return await CompleteWithEvaluationAsync(cancellationToken);

            var settingsNow = now < settings.UpdatedAtUtc ? settings.UpdatedAtUtc : now;
            settings.SetScheduleEnabled(isEnabled, settingsNow);
            await _settings.UpdateAsync(settings, cancellationToken);

            await WriteAuditAsync(
                AuditActor.Parent,
                AuditEventType.ScheduleChanged,
                isEnabled
                    ? "Parent enabled the protection schedule."
                    : "Parent disabled the protection schedule.",
                now,
                cancellationToken);

            if (isEnabled)
                return await CompleteWithEvaluationAsync(cancellationToken);

            var warnings = new List<string>();
            var protectionChanged = false;

            if (settings.ProtectionEnabled && settings.ProtectionSource == ProtectionSource.Schedule)
            {
                try
                {
                    var result = await _protection.DisableForScheduleAsync(cancellationToken);
                    protectionChanged = result.Changed;
                    warnings.AddRange(result.Warnings);
                }
                catch (OperationCanceledException)
                {
                    throw;
                }
                catch (ProtectionEngineException ex)
                {
                    _logger.Error(
                        LogCategories.Protection,
                        "Could not disable schedule-owned protection while turning the schedule off.",
                        ex);
                    warnings.Add(EngineDisableWarning);
                }
            }

            return new ScheduleOperationResult(
                await GetOverviewAsync(cancellationToken),
                protectionChanged,
                warnings);
        }
        finally
        {
            _gate.Release();
        }
    }

    public async Task<ScheduleEvaluationResult> EvaluateAsync(CancellationToken cancellationToken = default)
    {
        await _gate.WaitAsync(cancellationToken);
        try
        {
            return await EvaluateCoreAsync(cancellationToken);
        }
        finally
        {
            _gate.Release();
        }
    }

    private async Task<ScheduleEvaluationResult> EvaluateCoreAsync(CancellationToken cancellationToken)
    {
        var now = UtcNow();
        var clockJump = DetectClockJump(now);

        if (clockJump)
        {
            await WriteAuditAsync(
                AuditActor.System,
                AuditEventType.TamperDetected,
                "System clock jump detected; schedule enforcement skipped for this evaluation.",
                now,
                cancellationToken);
        }

        var settings = await _settings.GetAsync(cancellationToken);
        var windows = await _windows.ListAsync(cancellationToken);
        var warnings = new List<string>();
        var protectionChanged = false;

        if (settings is not null && settings.ScheduleEnabled)
        {
            var zone = _timeProvider.LocalTimeZone;
            var localNow = TimeZoneInfo.ConvertTimeFromUtc(now, zone);
            var active = ScheduleClock.FindActive(windows, localNow);

            if (!clockJump && await ClampOverrideAsync(settings, active, localNow, zone, now, cancellationToken))
                settings = await _settings.GetAsync(cancellationToken) ?? settings;

            var overridden = settings.IsScheduleOverridden(now);

            if (active is null)
            {
                if (!clockJump
                    && settings.ProtectionEnabled
                    && settings.ProtectionSource == ProtectionSource.Schedule)
                {
                    try
                    {
                        var result = await _protection.DisableForScheduleAsync(cancellationToken);
                        protectionChanged = result.Changed;
                        warnings.AddRange(result.Warnings);
                    }
                    catch (OperationCanceledException)
                    {
                        throw;
                    }
                    catch (ProtectionEngineException ex)
                    {
                        _logger.Error(
                            LogCategories.Protection,
                            "Could not auto-disable protection after the window ended.",
                            ex);
                        warnings.Add(EngineDisableWarning);
                    }

                    settings = await _settings.GetAsync(cancellationToken) ?? settings;
                }
            }
            else if (!overridden && !settings.ProtectionEnabled)
            {
                try
                {
                    var result = await _protection.EnableForScheduleAsync(cancellationToken);
                    protectionChanged = result.Changed;
                    warnings.AddRange(result.Warnings);
                }
                catch (OperationCanceledException)
                {
                    throw;
                }
                catch (ProtectionEngineException ex)
                {
                    _logger.Error(
                        LogCategories.Protection,
                        "Could not auto-enable protection for an active schedule window.",
                        ex);
                    warnings.Add(EngineEnableWarning);
                }

                settings = await _settings.GetAsync(cancellationToken) ?? settings;
            }
        }

        return new ScheduleEvaluationResult(
            protectionChanged,
            clockJump,
            BuildOverview(settings, windows, now),
            warnings);
    }

    /// <summary>
    /// Clears expired or meaningless overrides and clamps one that would outlive
    /// its window after a schedule edit (UC06: override must not exceed the window).
    /// </summary>
    private async Task<bool> ClampOverrideAsync(
        AppSettings settings,
        ScheduleWindow? active,
        DateTime localNow,
        TimeZoneInfo zone,
        DateTime nowUtc,
        CancellationToken cancellationToken)
    {
        if (settings.ScheduleOverrideUntilUtc is not { } until)
            return false;

        var settingsNow = nowUtc < settings.UpdatedAtUtc ? settings.UpdatedAtUtc : nowUtc;
        var windowEndUtc = active is null
            ? (DateTime?)null
            : ScheduleClock.GetWindowEndUtc(active, DateOnly.FromDateTime(localNow), zone);

        if (until <= nowUtc || windowEndUtc is null)
        {
            settings.ClearScheduleOverride(settingsNow);
            await _settings.UpdateAsync(settings, cancellationToken);
            return true;
        }

        if (until > windowEndUtc)
        {
            if (windowEndUtc.Value > settingsNow)
                settings.SetScheduleOverrideUntil(windowEndUtc.Value, settingsNow);
            else
                settings.ClearScheduleOverride(settingsNow);

            await _settings.UpdateAsync(settings, cancellationToken);
            return true;
        }

        return false;
    }

    private bool DetectClockJump(DateTime nowUtc)
    {
        var tick = _monotonic.GetTickCount();
        var jumped = false;

        if (_lastWallUtc is { } lastWall && _lastTick is { } lastTick)
        {
            var drift = (nowUtc - lastWall) - (tick - lastTick);
            jumped = drift.Duration() > MaxClockDrift;
        }

        _lastWallUtc = nowUtc;
        _lastTick = tick;
        return jumped;
    }

    private async Task<ScheduleOperationResult> CompleteWithEvaluationAsync(
        CancellationToken cancellationToken)
    {
        var evaluation = await EvaluateCoreAsync(cancellationToken);

        return new ScheduleOperationResult(
            evaluation.Overview,
            evaluation.ProtectionChanged,
            evaluation.Warnings);
    }

    private ScheduleOverview BuildOverview(
        AppSettings? settings,
        IReadOnlyList<ScheduleWindow> windows,
        DateTime nowUtc)
    {
        var localNow = TimeZoneInfo.ConvertTimeFromUtc(nowUtc, _timeProvider.LocalTimeZone);
        var active = ScheduleClock.FindActive(windows, localNow);

        return new ScheduleOverview(
            settings?.ScheduleEnabled == true,
            windows.Select(ToInfo).ToList(),
            active is null ? null : ToInfo(active),
            settings?.ScheduleOverrideUntilUtc,
            settings?.IsScheduleOverridden(nowUtc) == true,
            settings?.ProtectionEnabled == true,
            settings?.ProtectionSource ?? ProtectionSource.None);
    }

    private async Task<AppSettings> RequireSettingsAsync(CancellationToken cancellationToken) =>
        await _settings.GetAsync(cancellationToken)
        ?? throw new InvalidOperationException(
            "Onboarding settings are missing; complete onboarding before changing the schedule.");

    private async Task<ScheduleWindow> RequireWindowAsync(Guid id, CancellationToken cancellationToken) =>
        await _windows.GetAsync(ScheduleWindowId.From(id), cancellationToken)
        ?? throw new InvalidOperationException("Schedule window was not found.");

    private async Task WriteAuditAsync(
        AuditActor actor,
        AuditEventType eventType,
        string description,
        DateTime occurredAtUtc,
        CancellationToken cancellationToken)
    {
        try
        {
            var auditLog = AuditLog.Record(
                actor,
                eventType,
                description,
                occurredAtUtc,
                metadataJson: null,
                _correlation.Current);

            await _auditLogs.AddAsync(auditLog, cancellationToken);
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (Exception ex)
        {
            _logger.Error(
                LogCategories.Protection,
                $"Could not write audit event '{eventType}'.",
                ex);
        }
    }

    private static ScheduleWindowInfo ToInfo(ScheduleWindow window) =>
        new(
            window.Id.Value,
            window.Days,
            window.StartMinutes,
            window.EndMinutes,
            window.Label,
            window.IsEnabled);

    private static string Describe(ScheduleWindow window)
    {
        var label = string.IsNullOrWhiteSpace(window.Label) ? string.Empty : $" ({window.Label})";
        var enabled = window.IsEnabled ? string.Empty : " [disabled]";
        return $"{window.Days} {Format(window.StartMinutes)}-{Format(window.EndMinutes)}{label}{enabled}";
    }

    private static string Format(int minutes) => $"{minutes / 60:00}:{minutes % 60:00}";

    private DateTime UtcNow() => _timeProvider.GetUtcNow().UtcDateTime;
}
