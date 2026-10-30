using System.Text.Json;
using EduFocus.Domain.Entities;
using EduFocus.Domain.Enums;
using EduFocus.Ports.DTOs;
using EduFocus.Ports.Inbound;
using EduFocus.Ports.Outbound.Persistence;
using EduFocus.Ports.Outbound.Services;

namespace EduFocus.UseCases.Watchdog;

/// <summary>
/// One watchdog decision. Protection OFF (a PIN exit or onboarding) is the
/// authoritative "do not restart" signal; a persistent budget bounds crash
/// loops so a broken install can never spin the machine. Audit rows contain
/// only the reason, attempt and PIDs — never user content.
/// </summary>
public sealed class WatchdogService : IWatchdogUseCase
{
    private const string RestartDescription = "Watchdog restarted EduFocus.";
    private const string GaveUpDescription =
        "Watchdog stopped restarting EduFocus after reaching its restart budget.";

    private readonly IAppSettingsRepository _settings;
    private readonly IWatchdogHeartbeatStore _heartbeats;
    private readonly IWatchdogStateStore _states;
    private readonly IWatchdogProcessPort _processes;
    private readonly IAuditLogRepository _auditLogs;
    private readonly ICorrelationContext _correlation;
    private readonly IAppLogger _logger;
    private readonly TimeProvider _timeProvider;
    private readonly IMonotonicClockPort _monotonic;
    private readonly WatchdogOptions _options;

    private DateTime? _firstTickAtUtc;
    private TimeSpan? _lastMonotonic;
    private DateTime? _lastKillFailureLoggedAtUtc;

    public WatchdogService(
        IAppSettingsRepository settings,
        IWatchdogHeartbeatStore heartbeats,
        IWatchdogStateStore states,
        IWatchdogProcessPort processes,
        IAuditLogRepository auditLogs,
        ICorrelationContext correlation,
        IAppLogger logger,
        TimeProvider timeProvider,
        IMonotonicClockPort monotonic,
        WatchdogOptions options)
    {
        _settings = settings ?? throw new ArgumentNullException(nameof(settings));
        _heartbeats = heartbeats ?? throw new ArgumentNullException(nameof(heartbeats));
        _states = states ?? throw new ArgumentNullException(nameof(states));
        _processes = processes ?? throw new ArgumentNullException(nameof(processes));
        _auditLogs = auditLogs ?? throw new ArgumentNullException(nameof(auditLogs));
        _correlation = correlation ?? throw new ArgumentNullException(nameof(correlation));
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
        _timeProvider = timeProvider ?? throw new ArgumentNullException(nameof(timeProvider));
        _monotonic = monotonic ?? throw new ArgumentNullException(nameof(monotonic));
        _options = options ?? throw new ArgumentNullException(nameof(options));
    }

    public async Task<WatchdogTickResult> TickAsync(
        bool loginStart,
        CancellationToken cancellationToken = default)
    {
        var now = UtcNow();
        _firstTickAtUtc ??= now;

        var monotonicNow = _monotonic.GetTickCount();
        var resumed = _lastMonotonic is { } last &&
                      monotonicNow - last > _options.ResumeGapThreshold;
        _lastMonotonic = monotonicNow;

        var settings = await _settings.GetAsync(cancellationToken);

        if (settings is null || !settings.OnboardingCompleted)
        {
            _logger.Info(
                LogCategories.Watchdog,
                "The watchdog stops because onboarding is not complete.");
            return WatchdogTickResult.Stop(_options.PollInterval);
        }

        var state = await _states.ReadAsync(cancellationToken) ?? new WatchdogRestartState();
        var savedState = state;
        state = Normalize(state, now);

        if (!settings.ProtectionEnabled)
        {
            if (state != new WatchdogRestartState())
            {
                state = new WatchdogRestartState();
                await _states.WriteAsync(state, cancellationToken);
            }

            if (loginStart &&
                settings.StartWithWindows &&
                !_processes.IsAppInstanceRunning())
            {
                var loginProcess = _processes.StartApp();

                if (loginProcess is null)
                    _logger.Error(
                        LogCategories.Watchdog,
                        "The watchdog could not start EduFocus at login.");
                else
                    _logger.Info(
                        LogCategories.Watchdog,
                        FormattableString.Invariant(
                            $"EduFocus started at login by the watchdog (pid={loginProcess.ProcessId})."));

                return new WatchdogTickResult(
                    WatchdogTickAction.StartedAppAtLogin,
                    _options.PollInterval,
                    ProcessId: loginProcess?.ProcessId);
            }

            return WatchdogTickResult.Stop(_options.PollInterval);
        }

        var heartbeat = await _heartbeats.ReadAsync(cancellationToken);
        var processAlive = heartbeat is not null &&
                           _processes.IsProcessAlive(
                               heartbeat.ProcessId,
                               heartbeat.ProcessStartedAtUtc);
        var heartbeatFresh = heartbeat is not null &&
                             now - heartbeat.UpdatedAtUtc <= _options.HeartbeatStaleAfter;

        if (processAlive && heartbeatFresh)
        {
            state = ClearPending(state);

            if (state.RestartCount > 0 &&
                state.LastRestartAtUtc is { } lastRestart &&
                now - lastRestart >= _options.StableResetAfter)
            {
                state = new WatchdogRestartState();
                _logger.Info(
                    LogCategories.Watchdog,
                    "EduFocus stayed stable; the watchdog restart budget was reset.");
            }

            if (state != savedState)
                await _states.WriteAsync(state, cancellationToken);

            return WatchdogTickResult.Wait(_options.PollInterval);
        }

        if (processAlive && !heartbeatFresh)
        {
            if (resumed || now - _firstTickAtUtc < _options.StartupGrace)
            {
                _logger.Info(
                    LogCategories.Watchdog,
                    "Heartbeat is stale but the watchdog is still in its startup/resume grace.");
                return WatchdogTickResult.Wait(_options.PollInterval);
            }

            if (_processes.TryKill(heartbeat!.ProcessId, heartbeat.ProcessStartedAtUtc))
            {
                _logger.Warning(
                    LogCategories.Watchdog,
                    FormattableString.Invariant(
                        $"Watchdog killed hung EduFocus pid={heartbeat.ProcessId}; heartbeat was stale since {heartbeat.UpdatedAtUtc:u}."));

                state = state with
                {
                    PendingProcessId = null,
                    PendingProcessStartedAtUtc = null,
                    PendingReason = WatchdogRestartReason.Hang,
                    NextRestartAtUtc = now + _options.HangKillDelay
                };
                await _states.WriteAsync(state, cancellationToken);

                return new WatchdogTickResult(
                    WatchdogTickAction.KilledHang,
                    _options.HangKillDelay,
                    WatchdogRestartReason.Hang,
                    heartbeat.ProcessId);
            }

            if (_lastKillFailureLoggedAtUtc is null ||
                now - _lastKillFailureLoggedAtUtc >= TimeSpan.FromMinutes(1))
            {
                _lastKillFailureLoggedAtUtc = now;
                _logger.Error(
                    LogCategories.Watchdog,
                    "The watchdog could not stop the hung EduFocus process; it keeps running.");
            }

            return WatchdogTickResult.Wait(_options.PollInterval);
        }

        if (heartbeat is not null &&
            !processAlive &&
            settings.LastCleanShutdownAtUtc is { } cleanShutdown &&
            cleanShutdown >= heartbeat.ProcessStartedAtUtc)
        {
            _logger.Info(
                LogCategories.Watchdog,
                "The last EduFocus exit was clean; the watchdog stops without restarting it.");
            return WatchdogTickResult.Stop(_options.PollInterval);
        }

        if (_processes.IsAppInstanceRunning())
        {
            // A process owns the app mutex but has no usable heartbeat yet:
            // either it is still starting or it is starting a second instance
            // that will exit on its own. Never start a duplicate.
            return WatchdogTickResult.Wait(_options.PollInterval);
        }

        if (state.PendingProcessId is { } pendingPid)
        {
            if (_processes.IsProcessAlive(pendingPid, state.PendingProcessStartedAtUtc ?? DateTime.MinValue))
            {
                return WatchdogTickResult.Wait(_options.PollInterval);
            }

            state = state with
            {
                PendingProcessId = null,
                PendingProcessStartedAtUtc = null,
                PendingReason = WatchdogRestartReason.UnexpectedExit
            };
        }

        if (state.GaveUpUntilUtc is { } gaveUpUntil && now < gaveUpUntil)
        {
            _logger.Warning(
                LogCategories.Watchdog,
                FormattableString.Invariant(
                    $"Watchdog is in its give-up cooldown until {gaveUpUntil:u}."));
            return WatchdogTickResult.Stop(_options.PollInterval);
        }

        if (state.NextRestartAtUtc is { } nextRestart && now < nextRestart)
        {
            if (state != savedState)
                await _states.WriteAsync(state, cancellationToken);

            return WatchdogTickResult.Wait(nextRestart - now);
        }

        var windowStart = state.WindowStartedAtUtc ?? now;

        if (state.RestartCount >= _options.MaxRestartsPerWindow)
        {
            var gaveUpAt = state.GaveUpRecordedAtUtc is null ? now : state.GaveUpRecordedAtUtc;
            var cooldownEnd = windowStart + _options.RestartWindow;
            state = state with
            {
                GaveUpUntilUtc = cooldownEnd > now ? cooldownEnd : now + _options.RestartWindow,
                GaveUpRecordedAtUtc = gaveUpAt,
                NextRestartAtUtc = null
            };
            await _states.WriteAsync(state, cancellationToken);

            if (savedState.GaveUpRecordedAtUtc is null)
            {
                await TryAuditAsync(
                    AuditEventType.WatchdogGaveUp,
                    GaveUpDescription,
                    new { attempts = state.RestartCount, windowMinutes = _options.RestartWindow.TotalMinutes },
                    now,
                    cancellationToken);
            }

            _logger.Error(
                LogCategories.Watchdog,
                FormattableString.Invariant(
                    $"Watchdog gave up after {state.RestartCount} restart(s) in the budget window."));

            return new WatchdogTickResult(
                WatchdogTickAction.GaveUp,
                _options.PollInterval,
                state.PendingReason);
        }

        var reason = state.PendingReason ??
                     (heartbeat is not null
                         ? WatchdogRestartReason.UnexpectedExit
                         : WatchdogRestartReason.Recovery);

        var started = _processes.StartApp();

        if (started is null)
        {
            _logger.Error(
                LogCategories.Watchdog,
                "The watchdog could not start EduFocus; retrying in one minute.");
            return WatchdogTickResult.Wait(TimeSpan.FromMinutes(1));
        }

        var attempt = state.RestartCount + 1;
        var backoff = Backoff(attempt);

        state = state with
        {
            RestartCount = attempt,
            WindowStartedAtUtc = windowStart,
            LastRestartAtUtc = now,
            NextRestartAtUtc = now + backoff,
            GaveUpUntilUtc = null,
            GaveUpRecordedAtUtc = null,
            PendingProcessId = started.ProcessId,
            PendingProcessStartedAtUtc = started.StartedAtUtc,
            PendingReason = null
        };
        await _states.WriteAsync(state, cancellationToken);

        await TryAuditAsync(
            AuditEventType.WatchdogRestart,
            RestartDescription,
            new
            {
                reason = reason.ToString(),
                attempt,
                oldPid = heartbeat?.ProcessId,
                newPid = started.ProcessId
            },
            now,
            cancellationToken);

        _logger.Warning(
            LogCategories.Watchdog,
            FormattableString.Invariant(
                $"Watchdog restarted EduFocus (reason={reason}, attempt={attempt}, oldPid={(heartbeat is null ? "none" : heartbeat.ProcessId.ToString())}, newPid={started.ProcessId})."));

        return new WatchdogTickResult(
            WatchdogTickAction.StartedApp,
            _options.PollInterval,
            reason,
            started.ProcessId);
    }

    private WatchdogRestartState Normalize(WatchdogRestartState state, DateTime now)
    {
        if (state.WindowStartedAtUtc is { } windowStart &&
            now - windowStart >= _options.RestartWindow)
        {
            state = new WatchdogRestartState
            {
                PendingProcessId = state.PendingProcessId,
                PendingProcessStartedAtUtc = state.PendingProcessStartedAtUtc,
                PendingReason = state.PendingReason
            };
        }

        if (state.GaveUpUntilUtc is { } gaveUpUntil && now >= gaveUpUntil)
            state = new WatchdogRestartState();

        return state;
    }

    private static WatchdogRestartState ClearPending(WatchdogRestartState state) =>
        state.PendingProcessId is null && state.PendingReason is null
            ? state
            : state with
            {
                PendingProcessId = null,
                PendingProcessStartedAtUtc = null,
                PendingReason = null
            };

    private TimeSpan Backoff(int attempt)
    {
        if (attempt <= 1)
            return _options.FirstRestartDelay;

        var multiplier = Math.Pow(2, Math.Min(attempt - 1, 8));
        var milliseconds = _options.RestartBackoffStep.TotalMilliseconds * multiplier;
        var capped = Math.Min(milliseconds, _options.MaxRestartDelay.TotalMilliseconds);
        return TimeSpan.FromMilliseconds(capped);
    }

    private async Task TryAuditAsync(
        AuditEventType eventType,
        string description,
        object metadata,
        DateTime occurredAtUtc,
        CancellationToken cancellationToken)
    {
        try
        {
            var auditLog = AuditLog.Record(
                AuditActor.System,
                eventType,
                description,
                occurredAtUtc,
                JsonSerializer.Serialize(metadata),
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
                LogCategories.Watchdog,
                $"Could not write watchdog audit event '{eventType}'.",
                ex);
        }
    }

    private DateTime UtcNow() => _timeProvider.GetUtcNow().UtcDateTime;
}
