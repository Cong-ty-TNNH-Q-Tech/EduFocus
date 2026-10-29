using EduFocus.Domain.Entities;
using EduFocus.Domain.Enums;
using EduFocus.Ports.DTOs;
using EduFocus.Ports.Inbound;
using EduFocus.Ports.Outbound.Persistence;
using EduFocus.Ports.Outbound.Services;
using EduFocus.Ports.Protection;

namespace EduFocus.UseCases.Lifecycle;

/// <summary>
/// UC12 exit paths. A PIN exit is a clean shutdown: protection is removed,
/// the session is <c>Completed</c> and <c>LastCleanShutdownAtUtc</c> is set so a
/// watchdog would not restart the app. A Windows shutdown/logoff only marks the
/// session <c>Interrupted</c> with <c>Note = SystemShutdown</c>; protection and
/// the clean marker are left untouched because the next startup reconciles them.
/// </summary>
public sealed class AppLifecycleService : IAppLifecycleUseCase
{
    public const string SystemShutdownNote = "SystemShutdown";

    private const string SettingsMissing =
        "Onboarding settings are missing; complete onboarding before exiting EduFocus.";

    private const string CleanShutdownWarning =
        "Không thể ghi dấu thoát sạch. Lần khởi động sau sẽ dọn dẹp theo trạng thái đã lưu.";

    private const string EngineRemoveError =
        "Không thể gỡ chặn bảo vệ nên EduFocus vẫn đang chạy. Vui lòng thử lại.";

    private const string ExitError =
        "Không thể thoát an toàn lúc này. EduFocus vẫn đang chạy; vui lòng thử lại.";

    private readonly IParentModeUseCase _parentMode;
    private readonly IProtectionUseCase _protection;
    private readonly IStudySessionRepository _sessions;
    private readonly IStudySessionHeartbeatUseCase _heartbeat;
    private readonly IActivityUseCase _activity;
    private readonly IAttentionPipelineUseCase _attentionPipeline;
    private readonly IAttentionMonitorUseCase _attentionMonitor;
    private readonly IAppSettingsRepository _settings;
    private readonly IAuditLogRepository _auditLogs;
    private readonly ICorrelationContext _correlation;
    private readonly IAppLogger _logger;
    private readonly TimeProvider _timeProvider;

    public AppLifecycleService(
        IParentModeUseCase parentMode,
        IProtectionUseCase protection,
        IStudySessionRepository sessions,
        IStudySessionHeartbeatUseCase heartbeat,
        IActivityUseCase activity,
        IAttentionPipelineUseCase attentionPipeline,
        IAttentionMonitorUseCase attentionMonitor,
        IAppSettingsRepository settings,
        IAuditLogRepository auditLogs,
        ICorrelationContext correlation,
        IAppLogger logger,
        TimeProvider timeProvider)
    {
        _parentMode = parentMode ?? throw new ArgumentNullException(nameof(parentMode));
        _protection = protection ?? throw new ArgumentNullException(nameof(protection));
        _sessions = sessions ?? throw new ArgumentNullException(nameof(sessions));
        _heartbeat = heartbeat ?? throw new ArgumentNullException(nameof(heartbeat));
        _activity = activity ?? throw new ArgumentNullException(nameof(activity));
        _attentionPipeline = attentionPipeline ?? throw new ArgumentNullException(nameof(attentionPipeline));
        _attentionMonitor = attentionMonitor ?? throw new ArgumentNullException(nameof(attentionMonitor));
        _settings = settings ?? throw new ArgumentNullException(nameof(settings));
        _auditLogs = auditLogs ?? throw new ArgumentNullException(nameof(auditLogs));
        _correlation = correlation ?? throw new ArgumentNullException(nameof(correlation));
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
        _timeProvider = timeProvider ?? throw new ArgumentNullException(nameof(timeProvider));
    }

    public async Task<AppExitResult> ExitWithPinAsync(
        string pin,
        CancellationToken cancellationToken = default)
    {
        try
        {
            var login = await _parentMode.LoginAsync(pin, cancellationToken);

            if (login.Status != ParentAuthStatus.Success)
            {
                return new AppExitResult(
                    CanExit: false,
                    login.Status,
                    login.RemainingAttempts,
                    login.LockoutUntilUtc,
                    Warnings: [],
                    Error: null);
            }

            return await CleanExitAsync(login, cancellationToken);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception ex)
        {
            _logger.Error(LogCategories.App, "Clean exit failed; EduFocus keeps running.", ex);

            return new AppExitResult(
                CanExit: false,
                ParentAuthStatus.Success,
                ParentCredential.MaxFailedAttempts,
                LockoutUntilUtc: null,
                Warnings: [],
                Error: ex is ProtectionEngineException ? EngineRemoveError : ExitError);
        }
        finally
        {
            _parentMode.Lock();
        }
    }

    public async Task NotifySystemShutdownAsync(CancellationToken cancellationToken = default)
    {
        var now = UtcNow();

        // Stop the cadence first and reload the row so the shutdown interrupt
        // closes at the flushed heartbeat instead of a stale snapshot.
        await TryStopAttentionAsync();
        await TryStopActivityAsync();
        await TryStopHeartbeatAsync();

        try
        {
            var active = await _sessions.GetActiveAsync(cancellationToken);

            if (active is not null)
                await InterruptForShutdownAsync(active, now, cancellationToken);
        }
        catch (OperationCanceledException)
        {
            _logger.Warning(
                LogCategories.App,
                "Windows shutdown session recording was cancelled; the next startup will self-heal the session.");
            return;
        }
        catch (Exception ex)
        {
            _logger.Error(
                LogCategories.App,
                "Could not interrupt the active session for Windows shutdown; the next startup will self-heal it.",
                ex);
        }

        try
        {
            await TryWriteAuditAsync(
                AuditActor.System,
                AuditEventType.SystemShutdown,
                "Windows shutdown/logoff detected; protection stays enabled and recovers at next sign-in.",
                now,
                cancellationToken);
        }
        catch (OperationCanceledException)
        {
            _logger.Warning(
                LogCategories.App,
                "Windows shutdown audit was cancelled; the next startup will self-heal the session.");
        }
    }

    private async Task InterruptForShutdownAsync(
        StudySession active,
        DateTime now,
        CancellationToken cancellationToken)
    {
        try
        {
            active.SetNote(SystemShutdownNote, now);
        }
        catch (Exception ex)
        {
            // The note is informative; the interruption below must still run.
            _logger.Error(
                LogCategories.App,
                "Could not add the SystemShutdown note to the active session.",
                ex);
        }

        active.Interrupt();
        await _sessions.UpdateAsync(active, cancellationToken);

        await TryWriteAuditAsync(
            AuditActor.System,
            AuditEventType.SessionInterrupted,
            "Active study session interrupted by Windows shutdown.",
            now,
            cancellationToken);
    }

    private async Task<AppExitResult> CleanExitAsync(
        ParentAuthResult login,
        CancellationToken cancellationToken)
    {
        var disable = await _protection.DisableForExitAsync(cancellationToken);
        var warnings = new List<string>(disable.Warnings);
        var now = UtcNow();

        try
        {
            var settings = await _settings.GetAsync(cancellationToken)
                ?? throw new InvalidOperationException(SettingsMissing);

            settings.RecordCleanShutdown(now);
            await _settings.UpdateAsync(settings, cancellationToken);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception ex)
        {
            _logger.Error(LogCategories.App, "Could not record the clean-shutdown marker.", ex);
            warnings.Add(CleanShutdownWarning);
        }

        await TryWriteAuditAsync(
            AuditActor.Parent,
            AuditEventType.CleanExit,
            "Parent exited EduFocus with a valid PIN.",
            now,
            cancellationToken);

        return new AppExitResult(
            CanExit: true,
            login.Status,
            login.RemainingAttempts,
            login.LockoutUntilUtc,
            warnings,
            Error: null);
    }

    private async Task TryStopActivityAsync()
    {
        try
        {
            await _activity.StopAsync(CancellationToken.None);
        }
        catch (Exception ex)
        {
            _logger.Error(
                LogCategories.Session,
                "Could not stop activity sampling during Windows shutdown.",
                ex);
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
            _logger.Error(
                LogCategories.Attention,
                "Could not stop the attention pipeline during Windows shutdown.",
                ex);
        }

        try
        {
            await _attentionMonitor.StopAsync(CancellationToken.None);
        }
        catch (Exception ex)
        {
            _logger.Error(
                LogCategories.Attention,
                "Could not stop attention monitoring during Windows shutdown.",
                ex);
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
            _logger.Error(
                LogCategories.Session,
                "Could not stop the study-session heartbeat during Windows shutdown.",
                ex);
        }
    }

    private async Task TryWriteAuditAsync(
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
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception ex)
        {
            _logger.Error(
                LogCategories.App,
                $"Could not write audit event '{eventType}'.",
                ex);
        }
    }

    private DateTime UtcNow() => _timeProvider.GetUtcNow().UtcDateTime;
}
