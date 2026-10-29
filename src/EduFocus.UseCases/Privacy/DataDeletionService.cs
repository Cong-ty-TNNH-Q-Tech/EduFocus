using EduFocus.Domain.Entities;
using EduFocus.Domain.Enums;
using EduFocus.Ports.DTOs;
using EduFocus.Ports.Inbound;
using EduFocus.Ports.Outbound.Persistence;
using EduFocus.Ports.Outbound.Services;

namespace EduFocus.UseCases.Privacy;

/// <summary>
/// Irreversible delete-all from UC11 (issue #38). The parent must be in Parent
/// Mode, type the exact confirmation and re-enter the PIN. Study data is removed
/// in batches while Active sessions, credentials, settings and audit rows are
/// preserved; a partial failure is reported honestly and never as success.
/// </summary>
public sealed class DataDeletionService : IDataDeletionUseCase
{
    public const string RequiredConfirmation = "XÓA";

    private readonly IPrivacyDataRepository _data;
    private readonly IParentAuthUseCase _auth;
    private readonly IParentAuthorizer _authorizer;
    private readonly IAuditLogRepository _auditLogs;
    private readonly ICorrelationContext _correlation;
    private readonly IAppLogger _logger;
    private readonly TimeProvider _timeProvider;
    private readonly DataDeletionOptions _options;

    public DataDeletionService(
        IPrivacyDataRepository data,
        IParentAuthUseCase auth,
        IParentAuthorizer authorizer,
        IAuditLogRepository auditLogs,
        ICorrelationContext correlation,
        IAppLogger logger,
        TimeProvider timeProvider,
        DataDeletionOptions options)
    {
        _data = data ?? throw new ArgumentNullException(nameof(data));
        _auth = auth ?? throw new ArgumentNullException(nameof(auth));
        _authorizer = authorizer ?? throw new ArgumentNullException(nameof(authorizer));
        _auditLogs = auditLogs ?? throw new ArgumentNullException(nameof(auditLogs));
        _correlation = correlation ?? throw new ArgumentNullException(nameof(correlation));
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
        _timeProvider = timeProvider ?? throw new ArgumentNullException(nameof(timeProvider));

        if (options is null)
            throw new ArgumentNullException(nameof(options));
        if (options.BatchSize is < 1 or > 10_000)
            throw new ArgumentOutOfRangeException(nameof(options), "Batch size must be between 1 and 10,000.");

        _options = options;
    }

    public async Task<DataDeletionResult> DeleteAllAsync(
        DataDeletionRequest request,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request);
        _authorizer.EnsureAuthorized("Delete all study data");

        if (!string.Equals(
                request.ConfirmationText?.Trim(),
                RequiredConfirmation,
                StringComparison.Ordinal))
        {
            return Failure(DataDeletionStatus.ConfirmationMismatch);
        }

        var verification = await _auth.VerifyPinAsync(request.Pin, cancellationToken).ConfigureAwait(false);
        switch (verification.Status)
        {
            case ParentAuthStatus.NotConfigured:
                return Failure(DataDeletionStatus.NotConfigured);
            case ParentAuthStatus.LockedOut:
                return new DataDeletionResult(
                    DataDeletionStatus.LockedOut,
                    DataDeletionCounts.Empty,
                    [],
                    verification.RemainingAttempts,
                    verification.LockoutUntilUtc);
            case ParentAuthStatus.InvalidCredential:
                return new DataDeletionResult(
                    DataDeletionStatus.InvalidPin,
                    DataDeletionCounts.Empty,
                    [],
                    verification.RemainingAttempts,
                    verification.LockoutUntilUtc);
        }

        var failures = new List<string>();

        // Detail rows first so session deletes only cascade rows already counted
        // here; Active sessions are excluded by the repository SQL at every step.
        var samples = await DeleteAllAsync(
            "ActivitySamples",
            (batch, token) => _data.DeleteActivitySamplesBatchAsync(batch, token),
            failures,
            cancellationToken).ConfigureAwait(false);

        var events = await DeleteAllAsync(
            "AttentionEvents",
            (batch, token) => _data.DeleteAttentionEventsBatchAsync(batch, token),
            failures,
            cancellationToken).ConfigureAwait(false);

        var attempts = await DeleteAllAsync(
            "BlockedAttempts",
            (batch, token) => _data.DeleteBlockedAttemptsBatchAsync(batch, token),
            failures,
            cancellationToken).ConfigureAwait(false);

        var sessions = await DeleteAllAsync(
            "StudySessions",
            (batch, token) => _data.DeleteFinishedSessionsBatchAsync(batch, token),
            failures,
            cancellationToken).ConfigureAwait(false);

        try
        {
            await _data.VacuumAsync(cancellationToken).ConfigureAwait(false);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception ex)
        {
            // Deleted rows stay deleted; a failed reclaim only affects file size.
            _logger.Warning(LogCategories.Database, $"Delete-all vacuum failed ({ex.GetType().Name}).");
        }

        var counts = new DataDeletionCounts(sessions, samples, events, attempts);
        var now = UtcNow();

        try
        {
            await _auditLogs
                .AddAsync(
                    AuditLog.Record(
                        AuditActor.Parent,
                        AuditEventType.DataDeleted,
                        BuildDescription(counts, failures),
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
            _logger.Error(LogCategories.Database, "Could not write the delete-all audit entry.", ex);
        }

        var status = failures.Count == 0 ? DataDeletionStatus.Success : DataDeletionStatus.Incomplete;

        _logger.Info(
            LogCategories.Database,
            FormattableString.Invariant($"Delete-all removed {sessions} session(s), {samples} activity sample(s), {events} attention event(s) and {attempts} blocked attempt(s); status: {status}."));

        return new DataDeletionResult(
            status,
            counts,
            failures,
            verification.RemainingAttempts,
            LockoutUntilUtc: null);
    }

    private async Task<int> DeleteAllAsync(
        string table,
        Func<int, CancellationToken, Task<int>> deleteBatch,
        List<string> failures,
        CancellationToken cancellationToken)
    {
        var total = 0;

        try
        {
            while (true)
            {
                var deleted = await deleteBatch(_options.BatchSize, cancellationToken).ConfigureAwait(false);
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
                $"Delete-all failed for {table} after removing {total} row(s) ({ex.GetType().Name}).");
        }

        return total;
    }

    private static string BuildDescription(DataDeletionCounts counts, IReadOnlyList<string> failures)
    {
        var description = FormattableString.Invariant(
            $"Parent deleted all study data: {counts.Sessions} session(s), {counts.ActivitySamples} activity sample(s), {counts.AttentionEvents} attention event(s) and {counts.BlockedAttempts} blocked attempt(s).");

        if (failures.Count > 0)
            description += $" Incomplete: {string.Join(", ", failures)}.";

        return description;
    }

    private static DataDeletionResult Failure(DataDeletionStatus status) =>
        new(status, DataDeletionCounts.Empty, [], RemainingAttempts: 0, LockoutUntilUtc: null);

    private DateTime UtcNow() => _timeProvider.GetUtcNow().UtcDateTime;
}
