using System.Reflection;
using EduFocus.Domain.Entities;
using EduFocus.Domain.Enums;
using EduFocus.Infrastructure.ServiceControl;
using EduFocus.Ports.Inbound;
using EduFocus.Ports.Outbound.Persistence;
using EduFocus.Ports.Outbound.Services;
using EduFocus.SqliteDataAccess.Persistence;

namespace EduFocus.Service.Runtime;

/// <summary>
/// Owns protection while the UI is closed: it prepares the database, seeds the
/// built-in rules, reconciles the engine, evaluates the schedule every 30
/// seconds, runs retention and answers authenticated control commands.
/// Stopping the service keeps <c>ProtectionEnabled</c> untouched and never
/// removes the hosts section, so a stop is fail-closed; the next start
/// reconciles the engine and closes the interrupted session at its last beat.
/// </summary>
public sealed class ServiceRuntime : IAsyncDisposable
{
    private const string ServiceStoppedNote = "ServiceStopped";

    private readonly SqliteDatabaseInitializer _initializer;
    private readonly IBlockRuleSeedUseCase _seed;
    private readonly IProtectionUseCase _protection;
    private readonly IScheduleUseCase _schedule;
    private readonly IRetentionUseCase _retention;
    private readonly IStudySessionRepository _sessions;
    private readonly IStudySessionHeartbeatUseCase _heartbeat;
    private readonly IAuditLogRepository _auditLogs;
    private readonly ICorrelationContext _correlation;
    private readonly IAppLogger _logger;
    private readonly TimeProvider _timeProvider;
    private readonly ServiceControlServer _controlServer;
    private readonly ServiceRuntimeStatus _status;
    private readonly ServiceRuntimeOptions _options;
    private readonly CancellationTokenSource _stop = new();
    private readonly SemaphoreSlim _lifecycle = new(1, 1);

    private IDisposable? _correlationScope;
    private Task? _scheduleLoop;
    private Task? _retentionLoop;
    private string _lastScheduleWarnings = string.Empty;
    private bool _started;
    private bool _stopped;
    private bool _disposed;

    public ServiceRuntime(
        SqliteDatabaseInitializer initializer,
        IBlockRuleSeedUseCase seed,
        IProtectionUseCase protection,
        IScheduleUseCase schedule,
        IRetentionUseCase retention,
        IStudySessionRepository sessions,
        IStudySessionHeartbeatUseCase heartbeat,
        IAuditLogRepository auditLogs,
        ICorrelationContext correlation,
        IAppLogger logger,
        TimeProvider timeProvider,
        ServiceControlServer controlServer,
        ServiceRuntimeStatus status,
        ServiceRuntimeOptions options)
    {
        _initializer = initializer ?? throw new ArgumentNullException(nameof(initializer));
        _seed = seed ?? throw new ArgumentNullException(nameof(seed));
        _protection = protection ?? throw new ArgumentNullException(nameof(protection));
        _schedule = schedule ?? throw new ArgumentNullException(nameof(schedule));
        _retention = retention ?? throw new ArgumentNullException(nameof(retention));
        _sessions = sessions ?? throw new ArgumentNullException(nameof(sessions));
        _heartbeat = heartbeat ?? throw new ArgumentNullException(nameof(heartbeat));
        _auditLogs = auditLogs ?? throw new ArgumentNullException(nameof(auditLogs));
        _correlation = correlation ?? throw new ArgumentNullException(nameof(correlation));
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
        _timeProvider = timeProvider ?? throw new ArgumentNullException(nameof(timeProvider));
        _controlServer = controlServer ?? throw new ArgumentNullException(nameof(controlServer));
        _status = status ?? throw new ArgumentNullException(nameof(status));

        if (options is null)
            throw new ArgumentNullException(nameof(options));
        if (options.ScheduleEvaluationInterval < TimeSpan.FromSeconds(5)
            || options.ScheduleEvaluationInterval > TimeSpan.FromMinutes(10))
        {
            throw new ArgumentOutOfRangeException(
                nameof(options),
                "ScheduleEvaluationInterval must be between 5 seconds and 10 minutes.");
        }
        if (options.RetentionCheckInterval < TimeSpan.FromSeconds(10)
            || options.RetentionCheckInterval > TimeSpan.FromHours(1))
        {
            throw new ArgumentOutOfRangeException(
                nameof(options),
                "RetentionCheckInterval must be between 10 seconds and 1 hour.");
        }
        if (options.RetentionDailyHour is < 0 or > 23)
            throw new ArgumentOutOfRangeException(nameof(options), "RetentionDailyHour must be 0-23.");
        if (options.StopTimeout < TimeSpan.FromSeconds(1) || options.StopTimeout > TimeSpan.FromMinutes(1))
            throw new ArgumentOutOfRangeException(nameof(options), "StopTimeout must be between 1 and 60 seconds.");

        _options = options;
    }

    public DateTimeOffset? StartedAtUtc => _status.StartedAtUtc;

    public bool IsRunning => _started && !_stopped;

    public async Task StartAsync(CancellationToken cancellationToken = default)
    {
        await _lifecycle.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            if (_started)
                return;

            _correlationScope = _correlation.BeginScope(Guid.NewGuid());
            var version = typeof(ServiceRuntime).Assembly
                .GetCustomAttribute<AssemblyInformationalVersionAttribute>()?
                .InformationalVersion
                ?? typeof(ServiceRuntime).Assembly.GetName().Version?.ToString(3)
                ?? "0.0.0";

            _logger.Info(
                LogCategories.App,
                $"====== EduFocus protection service starting (v{version}, pid {Environment.ProcessId}) ======");

            try
            {
                await _initializer.InitializeAsync(cancellationToken).ConfigureAwait(false);

                try
                {
                    var seeded = await _seed.EnsureSeededAsync(cancellationToken).ConfigureAwait(false);
                    if (seeded.Changed)
                    {
                        _logger.Info(
                            LogCategories.Protection,
                            FormattableString.Invariant(
                                $"Built-in block rules v{seeded.SeedVersion} seeded: {seeded.Added} added, {seeded.Updated} updated."));
                    }
                }
                catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
                {
                    throw;
                }
                catch (Exception ex)
                {
                    // Existing rules stay usable; the next startup retries.
                    _logger.Error(LogCategories.Protection, "Built-in block rule seeding failed.", ex);
                }

                await _protection.ReconcileAsync(cancellationToken).ConfigureAwait(false);

                try
                {
                    await _schedule.EvaluateAsync(cancellationToken).ConfigureAwait(false);
                }
                catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
                {
                    throw;
                }
                catch (Exception ex)
                {
                    // The schedule loop retries every 30 seconds.
                    _logger.Error(LogCategories.Protection, "Initial schedule evaluation failed.", ex);
                }

                await _controlServer.StartAsync(cancellationToken).ConfigureAwait(false);

                var startedAt = _timeProvider.GetUtcNow();
                _status.MarkStarted(startedAt);
                _started = true;

                await WriteAuditAsync(
                    AuditEventType.ProtectionServiceStarted,
                    FormattableString.Invariant(
                        $"Protection service started (version {version}, process {Environment.ProcessId})."),
                    startedAt.UtcDateTime,
                    cancellationToken).ConfigureAwait(false);

                _scheduleLoop = Task.Run(() => ScheduleLoopAsync(_stop.Token), CancellationToken.None);
                _retentionLoop = Task.Run(() => RetentionLoopAsync(_stop.Token), CancellationToken.None);

                _logger.Info(LogCategories.App, "====== EduFocus protection service started ======");
            }
            catch
            {
                _started = false;
                await SafeStopControlServerAsync().ConfigureAwait(false);
                _correlationScope?.Dispose();
                _correlationScope = null;
                throw;
            }
        }
        finally
        {
            _lifecycle.Release();
        }
    }

    public async Task StopAsync(CancellationToken cancellationToken = default)
    {
        await _lifecycle.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            if (!_started || _stopped)
                return;

            _stopped = true;
            _logger.Info(LogCategories.App, "EduFocus protection service is stopping.");

            _stop.Cancel();

            await SafeStopControlServerAsync().ConfigureAwait(false);
            await WaitForLoopAsync(_scheduleLoop).ConfigureAwait(false);
            await WaitForLoopAsync(_retentionLoop).ConfigureAwait(false);

            await InterruptActiveSessionAsync().ConfigureAwait(false);

            await WriteAuditAsync(
                AuditEventType.ProtectionServiceStopped,
                "Protection service stopped; the persisted protection state and hosts section were kept for the next start.",
                UtcNow(),
                CancellationToken.None).ConfigureAwait(false);

            _logger.Info(LogCategories.App, "====== EduFocus protection service stopped ======");

            _correlationScope?.Dispose();
            _correlationScope = null;
        }
        finally
        {
            _lifecycle.Release();
        }
    }

    public async ValueTask DisposeAsync()
    {
        if (_disposed)
            return;

        _disposed = true;

        try
        {
            await StopAsync(CancellationToken.None).ConfigureAwait(false);
        }
        finally
        {
            _stop.Dispose();
            _lifecycle.Dispose();
        }
    }

    private async Task ScheduleLoopAsync(CancellationToken cancellationToken)
    {
        using var timer = new PeriodicTimer(_options.ScheduleEvaluationInterval);

        try
        {
            while (await timer.WaitForNextTickAsync(cancellationToken).ConfigureAwait(false))
            {
                try
                {
                    var result = await _schedule.EvaluateAsync(cancellationToken).ConfigureAwait(false);

                    var warningText = string.Join(" ", result.Warnings);
                    if (warningText != _lastScheduleWarnings)
                    {
                        if (warningText.Length > 0)
                            _logger.Warning(LogCategories.Protection, $"Schedule evaluation warnings: {warningText}");

                        _lastScheduleWarnings = warningText;
                    }
                }
                catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
                {
                    break;
                }
                catch (Exception ex)
                {
                    _logger.Error(LogCategories.Protection, "Schedule evaluation failed.", ex);
                }
            }
        }
        catch (OperationCanceledException)
        {
        }
    }

    private async Task RetentionLoopAsync(CancellationToken cancellationToken)
    {
        if (_options.RunRetentionAtStartup)
            await RunRetentionAsync(cancellationToken).ConfigureAwait(false);

        DateOnly? lastScheduledDate = null;
        using var timer = new PeriodicTimer(_options.RetentionCheckInterval);

        try
        {
            while (await timer.WaitForNextTickAsync(cancellationToken).ConfigureAwait(false))
            {
                var localNow = _timeProvider.GetLocalNow();

                if (localNow.Hour < _options.RetentionDailyHour)
                    continue;

                var today = DateOnly.FromDateTime(localNow.DateTime);
                if (lastScheduledDate == today)
                    continue;

                lastScheduledDate = today;
                await RunRetentionAsync(cancellationToken).ConfigureAwait(false);
            }
        }
        catch (OperationCanceledException)
        {
        }
    }

    private async Task RunRetentionAsync(CancellationToken cancellationToken)
    {
        try
        {
            var result = await _retention.RunOnceAsync(cancellationToken).ConfigureAwait(false);

            if (!result.Completed)
            {
                _logger.Warning(
                    LogCategories.Database,
                    $"Retention finished with issues: {string.Join(", ", result.Failures)}.");
            }
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception ex)
        {
            _logger.Error(LogCategories.Database, "Daily retention failed.", ex);
        }
    }

    private async Task InterruptActiveSessionAsync()
    {
        try
        {
            await _heartbeat.StopAsync(CancellationToken.None).ConfigureAwait(false);
        }
        catch (Exception ex)
        {
            _logger.Error(
                LogCategories.Session,
                "Could not stop the study-session heartbeat during service stop.",
                ex);
        }

        try
        {
            var active = await _sessions.GetActiveAsync(CancellationToken.None).ConfigureAwait(false);
            if (active is null)
                return;

            try
            {
                active.SetNote(ServiceStoppedNote, UtcNow());
            }
            catch (Exception ex)
            {
                _logger.Warning(
                    LogCategories.Session,
                    $"Could not add the ServiceStopped note ({ex.GetType().Name}).");
            }

            active.Interrupt();
            await _sessions.UpdateAsync(active, CancellationToken.None).ConfigureAwait(false);

            await WriteAuditAsync(
                AuditEventType.SessionInterrupted,
                "Active study session interrupted because the protection service stopped.",
                UtcNow(),
                CancellationToken.None).ConfigureAwait(false);
        }
        catch (Exception ex)
        {
            _logger.Error(
                LogCategories.Session,
                "Could not interrupt the active session during service stop.",
                ex);
        }
    }

    private async Task SafeStopControlServerAsync()
    {
        try
        {
            await _controlServer.StopAsync(CancellationToken.None).ConfigureAwait(false);
        }
        catch (Exception ex)
        {
            _logger.Warning(
                LogCategories.Protection,
                $"Could not stop the service control pipe ({ex.GetType().Name}).");
        }
    }

    private async Task WaitForLoopAsync(Task? loop)
    {
        if (loop is null)
            return;

        try
        {
            await loop.WaitAsync(_options.StopTimeout, CancellationToken.None).ConfigureAwait(false);
        }
        catch (TimeoutException)
        {
            _logger.Warning(LogCategories.App, "A protection service loop did not stop in time.");
        }
        catch (Exception ex)
        {
            _logger.Warning(
                LogCategories.App,
                $"A protection service loop stopped with an error ({ex.GetType().Name}).");
        }
    }

    private async Task WriteAuditAsync(
        AuditEventType eventType,
        string description,
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
                metadataJson: null,
                _correlation.Current);

            await _auditLogs.AddAsync(auditLog, cancellationToken).ConfigureAwait(false);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
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

    private DateTime UtcNow() => _timeProvider.GetUtcNow().UtcDateTime;
}
