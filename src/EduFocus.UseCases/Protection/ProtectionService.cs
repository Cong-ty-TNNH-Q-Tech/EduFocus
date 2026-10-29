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

    private const string AttentionEventWarning =
        "Không thể đóng attention event còn mở của phiên bị gián đoạn.";

    private readonly IAppSettingsRepository _settings;
    private readonly IStudySessionRepository _sessions;
    private readonly IScheduleWindowRepository _scheduleWindows;
    private readonly IAttentionEventRepository _attentionEvents;
    private readonly IProtectionEnginePort _engine;
    private readonly IStudySessionHeartbeatUseCase _heartbeat;
    private readonly IActivityUseCase _activity;
    private readonly IAttentionPipelineUseCase _attentionPipeline;
    private readonly IAttentionMonitorUseCase _attentionMonitor;
    private readonly IParentAuthorizer _authorizer;
    private readonly IAuditLogRepository _auditLogs;
    private readonly ICorrelationContext _correlation;
    private readonly IAppLogger _logger;
    private readonly TimeProvider _timeProvider;
    private readonly SemaphoreSlim _stateGate = new(1, 1);

    public ProtectionService(
        IAppSettingsRepository settings,
        IStudySessionRepository sessions,
        IScheduleWindowRepository scheduleWindows,
        IAttentionEventRepository attentionEvents,
        IProtectionEnginePort engine,
        IStudySessionHeartbeatUseCase heartbeat,
        IActivityUseCase activity,
        IAttentionPipelineUseCase attentionPipeline,
        IAttentionMonitorUseCase attentionMonitor,
        IParentAuthorizer authorizer,
        IAuditLogRepository auditLogs,
        ICorrelationContext correlation,
        IAppLogger logger,
        TimeProvider timeProvider)
    {
        _settings = settings ?? throw new ArgumentNullException(nameof(settings));
        _sessions = sessions ?? throw new ArgumentNullException(nameof(sessions));
        _scheduleWindows = scheduleWindows ?? throw new ArgumentNullException(nameof(scheduleWindows));
        _attentionEvents = attentionEvents ?? throw new ArgumentNullException(nameof(attentionEvents));
        _engine = engine ?? throw new ArgumentNullException(nameof(engine));
        _heartbeat = heartbeat ?? throw new ArgumentNullException(nameof(heartbeat));
        _activity = activity ?? throw new ArgumentNullException(nameof(activity));
        _attentionPipeline = attentionPipeline ?? throw new ArgumentNullException(nameof(attentionPipeline));
        _attentionMonitor = attentionMonitor ?? throw new ArgumentNullException(nameof(attentionMonitor));
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

        return await RunSerializedAsync(
            () => EnableCoreAsync(
                ProtectionSource.Manual,
                AuditActor.Parent,
                "Parent enabled protection.",
                allowTakingOwnership: true,
                cancellationToken),
            cancellationToken);
    }

    public Task<ProtectionOperationResult> EnableForScheduleAsync(CancellationToken cancellationToken = default) =>
        RunSerializedAsync(
            () => EnableCoreAsync(
                ProtectionSource.Schedule,
                AuditActor.System,
                "Protection enabled by the schedule.",
                allowTakingOwnership: false,
                cancellationToken),
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
        now = UtcNow();
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

            // The heartbeat starts before the persisted flag flips so an
            // enabled state is never left without a cadence. Activity sampling
            // and attention monitoring are best-effort and never block
            // protection.
            if (createdSession || !wasEnabled)
            {
                await _heartbeat.StartAsync(active.Id, active.LastHeartbeatAtUtc, cancellationToken);
                await TryStartActivityAsync(active);
                await TryStartAttentionAsync(active);
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
            await TryStopAttentionAsync();
            await TryStopActivityAsync();
            await TryStopHeartbeatAsync();

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

        return await RunSerializedAsync(
            () => DisableCoreAsync(
                AuditActor.Parent,
                "Parent disabled protection.",
                createScheduleOverride: true,
                scheduleOwnedOnly: false,
                clearScheduleOverride: false,
                cancellationToken),
            cancellationToken);
    }

    public async Task<ProtectionOperationResult> DisableForExitAsync(CancellationToken cancellationToken = default)
    {
        _authorizer.EnsureAuthorized("exit EduFocus");

        return await RunSerializedAsync(
            () => DisableCoreAsync(
                AuditActor.Parent,
                "Parent exited EduFocus and disabled protection.",
                createScheduleOverride: false,
                scheduleOwnedOnly: false,
                clearScheduleOverride: true,
                cancellationToken),
            cancellationToken);
    }

    public Task<ProtectionOperationResult> DisableForScheduleAsync(CancellationToken cancellationToken = default) =>
        RunSerializedAsync(
            () => DisableCoreAsync(
                AuditActor.System,
                "Protection disabled at the end of the scheduled window.",
                createScheduleOverride: false,
                scheduleOwnedOnly: true,
                clearScheduleOverride: false,
                cancellationToken),
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
        IReadOnlyList<string> warnings;

        try
        {
            // Flush the final sample and beat before the engine is torn down,
            // then reload the row so completion cannot overwrite them.
            await TryStopAttentionAsync();
            await TryStopActivityAsync();
            await _heartbeat.StopAsync(CancellationToken.None);
            active = await _sessions.GetActiveAsync(cancellationToken) ?? active;

            warnings = await RemoveEngineAsync(cancellationToken);

            var endedAt = UtcNow();

            if (active is not null && endedAt < active.LastHeartbeatAtUtc)
                endedAt = active.LastHeartbeatAtUtc;

            if (active is not null)
            {
                active.Complete(endedAt);
                await _sessions.UpdateAsync(active, cancellationToken);
            }

            now = endedAt;

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

            await TryRestartActivityAsync();
            await TryRestartHeartbeatAsync();
            await TryRestartAttentionAsync();
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

    public Task<ProtectionOperationResult> ReconcileAsync(CancellationToken cancellationToken = default) =>
        RunSerializedAsync(() => ReconcileCoreAsync(cancellationToken), cancellationToken);

    private async Task<ProtectionOperationResult> ReconcileCoreAsync(CancellationToken cancellationToken)
    {
        var now = UtcNow();
        var settings = await _settings.GetAsync(cancellationToken);

        if (settings is null)
            return new ProtectionOperationResult(
                new ProtectionStatus(false, null, MonitoringEnabled: true),
                Changed: false,
                []);

        await TryStopAttentionAsync();
        await TryStopActivityAsync();
        await TryStopHeartbeatAsync();

        var warnings = new List<string>();
        var active = await _sessions.GetActiveAsync(cancellationToken);
        var changed = false;

        if (active is not null)
        {
            active.Interrupt();
            await _sessions.UpdateAsync(active, cancellationToken);

            try
            {
                var closed = await _attentionEvents.CloseOpenAsync(
                    active.Id,
                    active.LastHeartbeatAtUtc,
                    cancellationToken);

                if (closed > 0)
                {
                    _logger.Info(
                        LogCategories.Attention,
                        $"Closed {closed} open attention event(s) at the last heartbeat.");
                }
            }
            catch (OperationCanceledException)
            {
                throw;
            }
            catch (Exception ex)
            {
                _logger.Error(
                    LogCategories.Protection,
                    "Could not close open attention events after a restart.",
                    ex);
                warnings.Add(AttentionEventWarning);
            }

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

            now = UtcNow();

            var recovered = StudySession.Start(
                now,
                LocalDate(now),
                SessionTrigger.Recovery,
                settings.MonitoringEnabled);

            await _sessions.AddAsync(recovered, cancellationToken);
            await _heartbeat.StartAsync(recovered.Id, recovered.LastHeartbeatAtUtc, cancellationToken);
            await TryStartActivityAsync(recovered);
            await TryStartAttentionAsync(recovered);

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

    private async Task TryStopHeartbeatAsync()
    {
        try
        {
            await _heartbeat.StopAsync(CancellationToken.None);
        }
        catch (Exception ex)
        {
            _logger.Error(LogCategories.Session, "Could not stop the study-session heartbeat.", ex);
        }
    }

    private async Task TryStopActivityAsync()
    {
        try
        {
            await _activity.StopAsync(CancellationToken.None);
        }
        catch (Exception ex)
        {
            _logger.Error(LogCategories.Session, "Could not stop activity sampling.", ex);
        }
    }

    private async Task TryStartActivityAsync(StudySession session)
    {
        try
        {
            if (_activity.IsRunning)
                return;

            await _activity.StartAsync(session.Id, CancellationToken.None);
        }
        catch (Exception ex)
        {
            // Activity tracking is observability only; protection must stay ON.
            _logger.Error(LogCategories.Session, "Could not start activity sampling.", ex);
        }
    }

    private async Task TryRestartActivityAsync()
    {
        try
        {
            if (_activity.IsRunning)
                return;

            var active = await _sessions.GetActiveAsync(CancellationToken.None);

            if (active is not null)
                await _activity.StartAsync(active.Id, CancellationToken.None);
        }
        catch (Exception ex)
        {
            _logger.Error(
                LogCategories.Session,
                "Could not restart activity sampling after a failed protection change.",
                ex);
        }
    }

    private async Task TryRestartHeartbeatAsync()
    {
        try
        {
            if (_heartbeat.IsRunning)
                return;

            var active = await _sessions.GetActiveAsync(CancellationToken.None);

            if (active is not null)
                await _heartbeat.StartAsync(active.Id, active.LastHeartbeatAtUtc, CancellationToken.None);
        }
        catch (Exception ex)
        {
            _logger.Error(
                LogCategories.Session,
                "Could not restart the study-session heartbeat after a failed protection change.",
                ex);
        }
    }

    private async Task TryStartAttentionAsync(StudySession session)
    {
        try
        {
            if (!session.MonitoringEnabled)
            {
                await TryStopAttentionAsync();
                return;
            }

            // Subscribe the monitor before the pipeline produces its first
            // sample; a failing pipeline is stopped again below.
            await _attentionMonitor.StartAsync(session.Id, CancellationToken.None);

            try
            {
                await _attentionPipeline.StartAsync(CancellationToken.None);
            }
            catch
            {
                await TryStopAttentionAsync();
                throw;
            }
        }
        catch (Exception ex)
        {
            // Camera monitoring is an overlay; protection must stay ON even
            // when the camera or the inference model is unavailable.
            _logger.Error(LogCategories.Attention, "Could not start attention monitoring.", ex);
        }
    }

    private async Task TryStopAttentionAsync()
    {
        try
        {
            if (_attentionPipeline.IsRunning)
                await _attentionPipeline.StopAsync(CancellationToken.None);
        }
        catch (Exception ex)
        {
            _logger.Error(LogCategories.Attention, "Could not stop the attention pipeline.", ex);
        }

        try
        {
            // Always stop the monitor: it drains queued samples and closes the
            // open attention event even when the pipeline already stopped.
            await _attentionMonitor.StopAsync(CancellationToken.None);
        }
        catch (Exception ex)
        {
            _logger.Error(LogCategories.Attention, "Could not stop attention monitoring.", ex);
        }
    }

    private async Task TryRestartAttentionAsync()
    {
        try
        {
            var active = await _sessions.GetActiveAsync(CancellationToken.None);

            if (active is not null)
                await TryStartAttentionAsync(active);
        }
        catch (Exception ex)
        {
            _logger.Error(
                LogCategories.Attention,
                "Could not restart attention monitoring after a failed protection change.",
                ex);
        }
    }

    private async Task<ProtectionOperationResult> RunSerializedAsync(
        Func<Task<ProtectionOperationResult>> operation,
        CancellationToken cancellationToken)
    {
        await _stateGate.WaitAsync(cancellationToken);

        try
        {
            return await operation();
        }
        finally
        {
            _stateGate.Release();
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
