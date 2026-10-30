using EduFocus.Domain.Entities;
using EduFocus.Domain.ValueObjects;
using EduFocus.Ports.DTOs;
using EduFocus.Ports.Inbound;
using EduFocus.Ports.Outbound.Persistence;
using EduFocus.Ports.Outbound.Services;

namespace EduFocus.UseCases.Protection;

/// <summary>
/// Keeps the session-bound overlay that must stay in the interactive user
/// session — camera attention and foreground activity — aligned with the state
/// the protection service owns. The service cannot reach the camera or the
/// foreground window from Session 0, so it exposes the active session and this
/// coordinator starts or stops the sampling loops around it. All failures are
/// logged and never propagate: monitoring is an overlay, not protection.
/// </summary>
public sealed class ServiceMonitoringCoordinator
{
    private readonly IStudySessionRepository _sessions;
    private readonly IAttentionMonitorUseCase _attentionMonitor;
    private readonly IAttentionPipelineUseCase _attentionPipeline;
    private readonly IActivityUseCase _activity;
    private readonly IAppLogger _logger;
    private readonly SemaphoreSlim _gate = new(1, 1);

    private StudySessionId? _sessionId;

    public ServiceMonitoringCoordinator(
        IStudySessionRepository sessions,
        IAttentionMonitorUseCase attentionMonitor,
        IAttentionPipelineUseCase attentionPipeline,
        IActivityUseCase activity,
        IAppLogger logger)
    {
        _sessions = sessions ?? throw new ArgumentNullException(nameof(sessions));
        _attentionMonitor = attentionMonitor ?? throw new ArgumentNullException(nameof(attentionMonitor));
        _attentionPipeline = attentionPipeline ?? throw new ArgumentNullException(nameof(attentionPipeline));
        _activity = activity ?? throw new ArgumentNullException(nameof(activity));
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
    }

    /// <summary>
    /// Matches the overlay to <paramref name="status"/>: enabled protection
    /// starts monitoring for the service's active session, a disabled status
    /// stops it.
    /// </summary>
    public async Task SyncAsync(ProtectionStatus status, CancellationToken cancellationToken = default)
    {
        await _gate.WaitAsync(cancellationToken);

        try
        {
            if (!status.IsEnabled)
            {
                await StopCoreAsync();
                return;
            }

            var session = await TryGetActiveSessionAsync(cancellationToken);
            if (session is null)
            {
                // A just-enabled protection creates its session in the same
                // service call; a null here means the state moved on. The next
                // poll reconciles again.
                await StopCoreAsync();
                return;
            }

            await StartCoreAsync(session);
        }
        finally
        {
            _gate.Release();
        }
    }

    /// <summary>
    /// Re-reads the active session after a failed remote change. When the
    /// service rejected the change the session is still running and monitoring
    /// resumes; when it committed the change the session is gone and nothing
    /// restarts.
    /// </summary>
    public async Task RestoreAsync(CancellationToken cancellationToken = default)
    {
        await _gate.WaitAsync(cancellationToken);

        try
        {
            var session = await TryGetActiveSessionAsync(cancellationToken);
            if (session is null)
            {
                await StopCoreAsync();
                return;
            }

            await StartCoreAsync(session);
        }
        finally
        {
            _gate.Release();
        }
    }

    public async Task StopAsync(CancellationToken cancellationToken = default)
    {
        await _gate.WaitAsync(cancellationToken);

        try
        {
            await StopCoreAsync();
        }
        finally
        {
            _gate.Release();
        }
    }

    private async Task<StudySession?> TryGetActiveSessionAsync(CancellationToken cancellationToken)
    {
        try
        {
            return await _sessions.GetActiveAsync(cancellationToken);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception ex)
        {
            _logger.Error(
                LogCategories.Attention,
                "Could not read the active session for service-mode monitoring.",
                ex);
            return null;
        }
    }

    private async Task StartCoreAsync(StudySession session)
    {
        if (_sessionId != session.Id)
        {
            await StopCoreAsync();
            _sessionId = session.Id;
        }

        if (session.MonitoringEnabled)
        {
            await EnsureAttentionAsync(session);
        }
        else if (_attentionMonitor.IsRunning || _attentionPipeline.IsRunning)
        {
            await StopAttentionAsync();
        }

        if (!_activity.IsRunning)
        {
            try
            {
                await _activity.StartAsync(session.Id, CancellationToken.None);
            }
            catch (Exception ex)
            {
                _logger.Error(
                    LogCategories.Session,
                    "Could not start activity tracking in service mode.",
                    ex);
            }
        }
    }

    private async Task EnsureAttentionAsync(StudySession session)
    {
        if (_attentionMonitor.IsRunning)
            return;

        try
        {
            // The monitor must subscribe before the pipeline produces its first
            // sample, mirroring the local protection flow.
            await _attentionMonitor.StartAsync(session.Id, CancellationToken.None);

            try
            {
                await _attentionPipeline.StartAsync(CancellationToken.None);
            }
            catch
            {
                await StopAttentionAsync();
                throw;
            }
        }
        catch (Exception ex)
        {
            _logger.Error(
                LogCategories.Attention,
                "Could not start attention monitoring in service mode.",
                ex);
        }
    }

    private async Task StopCoreAsync()
    {
        await StopAttentionAsync();

        try
        {
            await _activity.StopAsync(CancellationToken.None);
        }
        catch (Exception ex)
        {
            _logger.Error(
                LogCategories.Session,
                "Could not stop activity tracking in service mode.",
                ex);
        }

        _sessionId = null;
    }

    private async Task StopAttentionAsync()
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
                "Could not stop the attention pipeline in service mode.",
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
                "Could not stop attention monitoring in service mode.",
                ex);
        }
    }
}
