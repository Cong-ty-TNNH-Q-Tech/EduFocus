using EduFocus.Domain.Entities;
using EduFocus.Domain.Enums;
using EduFocus.Domain.Scheduling;
using EduFocus.Ports.DTOs;
using EduFocus.Ports.Inbound;
using EduFocus.Ports.Outbound.Persistence;
using EduFocus.Ports.Outbound.Services;
using EduFocus.Ports.Protection;

namespace EduFocus.UseCases.Protection;

/// <summary>
/// Protection ON/OFF state machine (UC03, UC04). The persisted
/// <c>ProtectionEnabled</c> flag is the source of truth: the engine is applied
/// before the flag turns ON and removed before it turns OFF, so a crash leaves
/// recoverable resources rather than an OPEN state with blocks still installed.
/// Manual toggles require an authenticated Parent Mode session.
/// </summary>
public sealed class ProtectionService : IProtectionUseCase
{
    private const string SettingsMissing =
        "Onboarding settings are missing; complete onboarding before enabling protection.";

    private const string EngineApplyWarning =
        "Không thể áp dụng đầy đủ engine bảo vệ. Trạng thái đã lưu sẽ được thử lại ở lần khởi động sau.";

    private const string EngineCleanupWarning =
        "Không thể dọn tài nguyên bảo vệ còn sót. Hãy kiểm tra hosts file và tiến trình EduFocus.";

    private readonly IAppSettingsRepository _settings;
    private readonly IStudySessionRepository _sessions;
    private readonly IScheduleWindowRepository _scheduleWindows;
    private readonly IProtectionEnginePort _engine;
    private readonly IParentAuthorizer _authorizer;
    private readonly IAuditLogRepository _auditLogs;
    private readonly ICorrelationContext _correlation;
    private readonly IAppLogger _logger;
    private readonly TimeProvider _timeProvider;

    public ProtectionService(
        IAppSettingsRepository settings,
        IStudySessionRepository sessions,
        IScheduleWindowRepository scheduleWindows,
        IProtectionEnginePort engine,
        IParentAuthorizer authorizer,
        IAuditLogRepository auditLogs,
        ICorrelationContext correlation,
        IAppLogger logger,
        TimeProvider timeProvider)
    {
        _settings = settings ?? throw new ArgumentNullException(nameof(settings));
        _sessions = sessions ?? throw new ArgumentNullException(nameof(sessions));
        _scheduleWindows = scheduleWindows ?? throw new ArgumentNullException(nameof(scheduleWindows));
        _engine = engine ?? throw new ArgumentNullException(nameof(engine));
        _authorizer = authorizer ?? throw new ArgumentNullException(nameof(authorizer));
        _auditLogs = auditLogs ?? throw new ArgumentNullException(nameof(auditLogs));
        _correlation = correlation ?? throw new ArgumentNullException(nameof(correlation));
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
        _timeProvider = timeProvider ?? throw new ArgumentNullException(nameof(timeProvider));
    }

    public async Task<ProtectionStatus> GetStatusAsync(CancellationToken cancellationToken = default)
    {
        var settings = await _settings.GetAsync(cancellationToken);
        var active = await _sessions.GetActiveAsync(cancellationToken);

        return new ProtectionStatus(
            settings?.ProtectionEnabled == true,
            active?.StartedAtUtc,
            settings?.MonitoringEnabled == true);
    }

    public async Task<ProtectionOperationResult> EnableAsync(CancellationToken cancellationToken = default)
    {
        _authorizer.EnsureAuthorized("enable protection");

        return await EnableCoreAsync(
            ProtectionSource.Manual,
            AuditActor.Parent,
            "Parent enabled protection.",
            allowTakingOwnership: true,
            cancellationToken);
    }

    public Task<ProtectionOperationResult> EnableForScheduleAsync(CancellationToken cancellationToken = default) =>
        EnableCoreAsync(
            ProtectionSource.Schedule,
            AuditActor.System,
            "Protection enabled by the schedule.",
            allowTakingOwnership: false,
            cancellationToken);

    private async Task<ProtectionOperationResult> EnableCoreAsync(
        ProtectionSource source,
        AuditActor actor,
        string description,
        bool allowTakingOwnership,
        CancellationToken cancellationToken)
    {
        var now = UtcNow();

        var settings = await _settings.GetAsync(cancellationToken)
            ?? throw new InvalidOperationException(SettingsMissing);

        var active = await _sessions.GetActiveAsync(cancellationToken);

        if (source == ProtectionSource.Schedule && !settings.ScheduleEnabled)
            return new ProtectionOperationResult(CreateStatus(settings, active), Changed: false, []);

        var wasEnabled = settings.ProtectionEnabled;
        var sourceChanged = wasEnabled && settings.ProtectionSource != source;

        if (wasEnabled && (!allowTakingOwnership || !sourceChanged))
            return new ProtectionOperationResult(CreateStatus(settings, active), Changed: false, []);

        var warnings = wasEnabled ? [] : await ApplyEngineAsync(cancellationToken);
        var createdSession = false;

        try
        {
            if (active is null)
            {
                active = StudySession.Start(
                    now,
                    LocalDate(now),
                    source == ProtectionSource.Schedule ? SessionTrigger.Schedule : SessionTrigger.Manual,
                    settings.MonitoringEnabled);

                await _sessions.AddAsync(active, cancellationToken);
                createdSession = true;
            }

            settings.EnableProtection(now, source);
            await _settings.UpdateAsync(settings, cancellationToken);
            await WriteAuditAsync(
                actor,
                AuditEventType.ProtectionOn,
                description,
                now,
                cancellationToken);
        }
        catch
        {
            if (createdSession)
                await TryAbortSessionAsync(active!);

            if (!wasEnabled)
                await TryRemoveEngineAsync();

            throw;
        }

        return new ProtectionOperationResult(
            CreateStatus(settings, active),
            Changed: createdSession || !wasEnabled || sourceChanged,
            warnings);
    }

    public async Task<ProtectionOperationResult> DisableAsync(CancellationToken cancellationToken = default)
    {
        _authorizer.EnsureAuthorized("disable protection");

        return await DisableCoreAsync(
            AuditActor.Parent,
            "Parent disabled protection.",
            createScheduleOverride: true,
            scheduleOwnedOnly: false,
            clearScheduleOverride: false,
            cancellationToken);
    }

    public async Task<ProtectionOperationResult> DisableForExitAsync(CancellationToken cancellationToken = default)
    {
        _authorizer.EnsureAuthorized("exit EduFocus");

        return await DisableCoreAsync(
            AuditActor.Parent,
            "Parent exited EduFocus and disabled protection.",
            createScheduleOverride: false,
            scheduleOwnedOnly: false,
            clearScheduleOverride: true,
            cancellationToken);
    }

    public Task<ProtectionOperationResult> DisableForScheduleAsync(CancellationToken cancellationToken = default) =>
        DisableCoreAsync(
            AuditActor.System,
            "Protection disabled at the end of the scheduled window.",
            createScheduleOverride: false,
            scheduleOwnedOnly: true,
            clearScheduleOverride: false,
            cancellationToken);

    private async Task<ProtectionOperationResult> DisableCoreAsync(
        AuditActor actor,
        string description,
        bool createScheduleOverride,
        bool scheduleOwnedOnly,
        bool clearScheduleOverride,
        CancellationToken cancellationToken)
    {
        var now = UtcNow();

        var settings = await _settings.GetAsync(cancellationToken)
            ?? throw new InvalidOperationException(SettingsMissing);

        var active = await _sessions.GetActiveAsync(cancellationToken);
        var overrideToClear = clearScheduleOverride && settings.ScheduleOverrideUntilUtc is not null;

        if (scheduleOwnedOnly
            && (!settings.ProtectionEnabled || settings.ProtectionSource != ProtectionSource.Schedule))
        {
            return new ProtectionOperationResult(CreateStatus(settings, active), Changed: false, []);
        }

        if (!settings.ProtectionEnabled && active is null)
        {
            if (!overrideToClear)
                return new ProtectionOperationResult(CreateStatus(settings, null), Changed: false, []);

            settings.ClearScheduleOverride(now);
            await _settings.UpdateAsync(settings, cancellationToken);

            return new ProtectionOperationResult(CreateStatus(settings, null), Changed: true, []);
        }

        var overrideUntil = createScheduleOverride
            ? await ResolveScheduleOverrideEndAsync(settings, now, cancellationToken)
            : null;

        var wasEnabled = settings.ProtectionEnabled;
        var warnings = await RemoveEngineAsync(cancellationToken);

        try
        {
            if (active is not null)
            {
                active.Complete(now);
                await _sessions.UpdateAsync(active, cancellationToken);
            }

            if (wasEnabled)
            {
                if (overrideUntil is { } until)
                {
                    settings.DisableProtectionWithScheduleOverride(until, now);
                    description = $"{description} Schedule override until {until:u}.";
                }
                else
                {
                    settings.DisableProtection(now);
                }

                await _settings.UpdateAsync(settings, cancellationToken);
                await WriteAuditAsync(actor, AuditEventType.ProtectionOff, description, now, cancellationToken);
            }
            else if (overrideToClear)
            {
                settings.ClearScheduleOverride(now);
                await _settings.UpdateAsync(settings, cancellationToken);
            }
        }
        catch
        {
            if (wasEnabled)
                await TryReapplyEngineAsync();

            throw;
        }

        return new ProtectionOperationResult(CreateStatus(settings, null), Changed: true, warnings);
    }

    private async Task<DateTime?> ResolveScheduleOverrideEndAsync(
        AppSettings settings,
        DateTime nowUtc,
        CancellationToken cancellationToken)
    {
        if (!settings.ScheduleEnabled)
            return null;

        var windows = await _scheduleWindows.ListAsync(cancellationToken);
        var zone = _timeProvider.LocalTimeZone;
        var localNow = TimeZoneInfo.ConvertTimeFromUtc(nowUtc, zone);
        var activeWindow = ScheduleClock.FindActive(windows, localNow);

        if (activeWindow is null)
            return null;

        var windowEndUtc = ScheduleClock.GetWindowEndUtc(
            activeWindow,
            DateOnly.FromDateTime(localNow),
            zone);

        return windowEndUtc > nowUtc ? windowEndUtc : null;
    }

    public async Task<ProtectionOperationResult> ReconcileAsync(CancellationToken cancellationToken = default)
    {
        var now = UtcNow();
        var settings = await _settings.GetAsync(cancellationToken);

        if (settings is null)
            return new ProtectionOperationResult(
                new ProtectionStatus(false, null, MonitoringEnabled: true),
                Changed: false,
                []);

        var warnings = new List<string>();
        var active = await _sessions.GetActiveAsync(cancellationToken);
        var changed = false;

        if (active is not null)
        {
            active.Interrupt();
            await _sessions.UpdateAsync(active, cancellationToken);
            await WriteAuditAsync(
                AuditActor.System,
                AuditEventType.SessionInterrupted,
                "Active study session closed at its last heartbeat after a restart.",
                active.LastHeartbeatAtUtc,
                cancellationToken);

            changed = true;
        }

        if (settings.ProtectionEnabled)
        {
            try
            {
                warnings.AddRange(await _engine.ReconcileAsync(true, cancellationToken));
            }
            catch (OperationCanceledException)
            {
                throw;
            }
            catch (Exception ex)
            {
                _logger.Error(
                    LogCategories.Protection,
                    "Could not reconcile the protection engine at startup.",
                    ex);
                warnings.Add(EngineApplyWarning);
            }

            var recovered = StudySession.Start(
                now,
                LocalDate(now),
                SessionTrigger.Recovery,
                settings.MonitoringEnabled);

            await _sessions.AddAsync(recovered, cancellationToken);
            await WriteAuditAsync(
                AuditActor.System,
                AuditEventType.SessionRecovered,
                "Protection was restored after restart; a recovery session started.",
                now,
                cancellationToken);

            return new ProtectionOperationResult(
                CreateStatus(settings, recovered),
                Changed: true,
                warnings);
        }

        try
        {
            warnings.AddRange(await _engine.ReconcileAsync(false, cancellationToken));
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (Exception ex)
        {
            _logger.Error(
                LogCategories.Protection,
                "Could not clean up protection resources at startup.",
                ex);
            warnings.Add(EngineCleanupWarning);
        }

        return new ProtectionOperationResult(CreateStatus(settings, null), changed, warnings);
    }

    private async Task<IReadOnlyList<string>> ApplyEngineAsync(CancellationToken cancellationToken)
    {
        try
        {
            return await _engine.ApplyAsync(cancellationToken);
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (Exception ex)
        {
            throw new ProtectionEngineException("apply", "Could not apply protection.", ex);
        }
    }

    private async Task<IReadOnlyList<string>> RemoveEngineAsync(CancellationToken cancellationToken)
    {
        try
        {
            return await _engine.RemoveAsync(cancellationToken);
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (Exception ex)
        {
            throw new ProtectionEngineException("remove", "Could not remove protection.", ex);
        }
    }

    private async Task TryRemoveEngineAsync()
    {
        try
        {
            await _engine.RemoveAsync(CancellationToken.None);
        }
        catch (Exception ex)
        {
            _logger.Error(LogCategories.Protection, "Protection rollback could not remove the engine.", ex);
        }
    }

    private async Task TryReapplyEngineAsync()
    {
        try
        {
            await _engine.ApplyAsync(CancellationToken.None);
        }
        catch (Exception ex)
        {
            _logger.Error(LogCategories.Protection, "Protection rollback could not reapply the engine.", ex);
        }
    }

    private async Task TryAbortSessionAsync(StudySession session)
    {
        try
        {
            session.Abort(UtcNow());
            await _sessions.UpdateAsync(session, CancellationToken.None);
        }
        catch (Exception ex)
        {
            _logger.Error(LogCategories.Protection, "Protection rollback could not abort the new session.", ex);
        }
    }

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

    private static ProtectionStatus CreateStatus(AppSettings settings, StudySession? active) =>
        new(settings.ProtectionEnabled, active?.StartedAtUtc, settings.MonitoringEnabled);

    private DateTime UtcNow() => _timeProvider.GetUtcNow().UtcDateTime;

    private DateOnly LocalDate(DateTime utcNow) =>
        DateOnly.FromDateTime(TimeZoneInfo.ConvertTimeFromUtc(utcNow, _timeProvider.LocalTimeZone));
}
