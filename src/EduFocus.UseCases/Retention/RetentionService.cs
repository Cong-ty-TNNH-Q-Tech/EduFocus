using EduFocus.Domain.Entities;
using EduFocus.Domain.Enums;
using EduFocus.Ports.DTOs;
using EduFocus.Ports.Inbound;
using EduFocus.Ports.Outbound.Persistence;
using EduFocus.Ports.Outbound.Services;

namespace EduFocus.UseCases.Retention;

/// <summary>
/// Applies the retention windows stored in <see cref="AppSettings"/> (UC11,
/// issue #29). Each table is deleted in short batches; a failing table does not
/// stop the others, and the run is recorded with honest counts. Active sessions
/// and their detailed rows are never deleted.
/// </summary>
public sealed class RetentionService : IRetentionUseCase
{
    private const string SettingsMissing =
        "Retention skipped because application settings are missing.";

    private readonly IRetentionRepository _retention;
    private readonly IAppSettingsRepository _settings;
    private readonly IAuditLogRepository _auditLogs;
    private readonly ICorrelationContext _correlation;
    private readonly IAppLogger _logger;
    private readonly TimeProvider _timeProvider;
    private readonly RetentionOptions _options;
    private readonly SemaphoreSlim _gate = new(1, 1);

    public RetentionService(
        IRetentionRepository retention,
        IAppSettingsRepository settings,
        IAuditLogRepository auditLogs,
        ICorrelationContext correlation,
        IAppLogger logger,
        TimeProvider timeProvider,
        RetentionOptions options)
    {
        _retention = retention ?? throw new ArgumentNullException(nameof(retention));
        _settings = settings ?? throw new ArgumentNullException(nameof(settings));
        _auditLogs = auditLogs ?? throw new ArgumentNullException(nameof(auditLogs));
        _correlation = correlation ?? throw new ArgumentNullException(nameof(correlation));
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
        _timeProvider = timeProvider ?? throw new ArgumentNullException(nameof(timeProvider));

        if (options is null)
            throw new ArgumentNullException(nameof(options));
        if (options.BatchSize is < 1 or > 10_000)
            throw new ArgumentOutOfRangeException(nameof(options), "Batch size must be between 1 and 10,000.");
        if (options.AuditRetentionDays is < 1 or > 3650)
            throw new ArgumentOutOfRangeException(nameof(options), "Audit retention must be between 1 and 3650 days.");

        _options = options;
    }

    public async Task<RetentionRunResult> RunOnceAsync(CancellationToken cancellationToken = default)
    {
        await _gate.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            return await RunCoreAsync(cancellationToken).ConfigureAwait(false);
        }
        finally
        {
            _gate.Release();
        }
    }

    private async Task<RetentionRunResult> RunCoreAsync(CancellationToken cancellationToken)
    {
        var now = UtcNow();

        AppSettings? settings;
        try
        {
            settings = await _settings.GetAsync(cancellationToken).ConfigureAwait(false);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception ex)
        {
            _logger.Error(LogCategories.Database, "Could not read retention settings.", ex);
            return EmptyResult(now, "Settings");
        }

        if (settings is null)
        {
            _logger.Warning(LogCategories.Database, SettingsMissing);
            return EmptyResult(now, "Settings");
        }

        var failures = new List<string>();
        var activityCutoff = now.AddDays(-settings.ActivityRetentionDays);
        var eventCutoff = now.AddDays(-settings.EventRetentionDays);
        var sessionCutoff = now.AddDays(-settings.SessionRetentionDays);
        var auditCutoff = now.AddDays(-_options.AuditRetentionDays);

        // Detail tables first so session summaries survive until their own
        // (longer) window, and session deletes only cascade what remains.
        var samples = await DeleteAllAsync(
            "ActivitySamples",
            (cutoff, token) => _retention.DeleteActivitySamplesBeforeAsync(cutoff, _options.BatchSize, token),
            activityCutoff,
            failures,
            cancellationToken).ConfigureAwait(false);

        var events = await DeleteAllAsync(
            "AttentionEvents",
            (cutoff, token) => _retention.DeleteAttentionEventsBeforeAsync(cutoff, _options.BatchSize, token),
            eventCutoff,
            failures,
            cancellationToken).ConfigureAwait(false);

        var attempts = await DeleteAllAsync(
            "BlockedAttempts",
            (cutoff, token) => _retention.DeleteBlockedAttemptsBeforeAsync(cutoff, _options.BatchSize, token),
            eventCutoff,
            failures,
            cancellationToken).ConfigureAwait(false);

        var auditLogs = await DeleteAllAsync(
            "AuditLogs",
            (cutoff, token) => _retention.DeleteAuditLogsBeforeAsync(cutoff, _options.BatchSize, token),
            auditCutoff,
            failures,
            cancellationToken).ConfigureAwait(false);

        var sessions = await DeleteAllAsync(
            "StudySessions",
            (cutoff, token) => _retention.DeleteFinishedSessionsBeforeAsync(cutoff, _options.BatchSize, token),
            sessionCutoff,
            failures,
            cancellationToken).ConfigureAwait(false);

        try
        {
            await _retention.VacuumAsync(cancellationToken).ConfigureAwait(false);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception ex)
        {
            failures.Add("Vacuum");
            _logger.Warning(LogCategories.Database, $"Retention vacuum failed ({ex.GetType().Name}).");
        }

        var counts = new RetentionCounts(samples, events, attempts, sessions, auditLogs);
        var description = BuildDescription(counts, failures);

        try
        {
            await _auditLogs
                .AddAsync(
                    AuditLog.Record(
                        AuditActor.System,
                        AuditEventType.RetentionApplied,
                        description,
                        now,
                        correlationId: _correlation.Current),
                    cancellationToken)
                .ConfigureAwait(false);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception ex)
        {
            failures.Add("AuditLog");
            _logger.Error(LogCategories.Database, "Could not write the retention audit entry.", ex);
        }

        var result = new RetentionRunResult(now, counts, failures.Count == 0, failures);

        _logger.Info(
            LogCategories.Database,
            FormattableString.Invariant($"Retention deleted {samples} activity sample(s), {events} attention event(s), {attempts} blocked attempt(s), {sessions} session(s) and {auditLogs} audit row(s); completed: {result.Completed}."));

        return result;
    }

    private async Task<int> DeleteAllAsync(
        string table,
        Func<DateTime, CancellationToken, Task<int>> deleteBatch,
        DateTime cutoffUtc,
        List<string> failures,
        CancellationToken cancellationToken)
    {
        var total = 0;

        try
        {
            while (true)
            {
                var deleted = await deleteBatch(cutoffUtc, cancellationToken).ConfigureAwait(false);
                total += deleted;

                if (deleted < _options.BatchSize)
                    break;
            }
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception ex)
        {
            failures.Add(table);
            _logger.Error(
                LogCategories.Database,
                $"Retention failed for {table} after deleting {total} row(s) ({ex.GetType().Name}).");
        }

        return total;
    }

    private static string BuildDescription(RetentionCounts counts, IReadOnlyList<string> failures)
    {
        var description = FormattableString.Invariant($"Retention deleted {counts.ActivitySamples} activity sample(s), {counts.AttentionEvents} attention event(s), {counts.BlockedAttempts} blocked attempt(s), {counts.Sessions} session(s) and {counts.AuditLogs} audit row(s).");

        if (failures.Count > 0)
            description += $" Incomplete: {string.Join(", ", failures)}.";

        return description;
    }

    private RetentionRunResult EmptyResult(DateTime now, string failure) =>
        new(now, RetentionCounts.Empty, Completed: false, [failure]);

    private DateTime UtcNow() => _timeProvider.GetUtcNow().UtcDateTime;
}
