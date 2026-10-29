using EduFocus.Ports.DTOs;
using EduFocus.Ports.Outbound.Persistence;

namespace EduFocus.UseCases.Tests.TestSupport;

internal sealed class FakePrivacyDataRepository : IPrivacyDataRepository
{
    public int Sessions { get; set; }

    public int ActiveSessions { get; set; }

    public int ActivitySamples { get; set; }

    public int AttentionEvents { get; set; }

    public int BlockedAttempts { get; set; }

    public long DatabaseSizeBytes { get; set; } = 16_384;

    public HashSet<string> FailingTables { get; } = new(StringComparer.Ordinal);

    public List<string> DeletedTables { get; } = [];

    public bool ThrowOnVacuum { get; set; }

    public int VacuumCalls { get; private set; }

    public Action<string>? AfterDelete { get; set; }

    public Task<PrivacyDataCounts> GetCountsAsync(CancellationToken cancellationToken = default) =>
        Task.FromResult(new PrivacyDataCounts(
            Sessions,
            ActiveSessions,
            ActivitySamples,
            AttentionEvents,
            BlockedAttempts));

    public Task<long> GetDatabaseSizeBytesAsync(CancellationToken cancellationToken = default) =>
        Task.FromResult(DatabaseSizeBytes);

    public Task<int> DeleteActivitySamplesBatchAsync(
        int batchSize,
        CancellationToken cancellationToken = default) =>
        DeleteBatch(
            "ActivitySamples",
            batchSize,
            () => ActivitySamples,
            value => ActivitySamples = value,
            cancellationToken);

    public Task<int> DeleteAttentionEventsBatchAsync(
        int batchSize,
        CancellationToken cancellationToken = default) =>
        DeleteBatch(
            "AttentionEvents",
            batchSize,
            () => AttentionEvents,
            value => AttentionEvents = value,
            cancellationToken);

    public Task<int> DeleteBlockedAttemptsBatchAsync(
        int batchSize,
        CancellationToken cancellationToken = default) =>
        DeleteBatch(
            "BlockedAttempts",
            batchSize,
            () => BlockedAttempts,
            value => BlockedAttempts = value,
            cancellationToken);

    public Task<int> DeleteFinishedSessionsBatchAsync(
        int batchSize,
        CancellationToken cancellationToken = default) =>
        DeleteBatch(
            "StudySessions",
            batchSize,
            () => Sessions - ActiveSessions,
            value => Sessions = value + ActiveSessions,
            cancellationToken);

    public Task VacuumAsync(CancellationToken cancellationToken = default)
    {
        VacuumCalls++;

        if (ThrowOnVacuum)
            throw new InvalidOperationException("VACUUM failed.");

        return Task.CompletedTask;
    }

    private Task<int> DeleteBatch(
        string table,
        int batchSize,
        Func<int> read,
        Action<int> write,
        CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();

        if (batchSize <= 0)
            throw new ArgumentOutOfRangeException(nameof(batchSize));

        if (FailingTables.Contains(table))
            throw new InvalidOperationException($"{table} is unavailable.");

        var take = Math.Min(batchSize, read());
        if (take == 0)
            return Task.FromResult(0);

        write(read() - take);
        DeletedTables.Add(FormattableString.Invariant($"{table}:{take}"));
        AfterDelete?.Invoke(table);

        return Task.FromResult(take);
    }
}
