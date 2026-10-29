using EduFocus.Ports.Outbound.Persistence;

namespace EduFocus.UseCases.Tests.TestSupport;

internal sealed class FakeRetentionRepository : IRetentionRepository
{
    public Queue<int> ActivityBatches { get; } = new();

    public Queue<int> EventBatches { get; } = new();

    public Queue<int> AttemptBatches { get; } = new();

    public Queue<int> SessionBatches { get; } = new();

    public Queue<int> AuditBatches { get; } = new();

    public HashSet<int> FailActivityCall { get; } = [];

    public DateTime? ActivityCutoff { get; private set; }

    public DateTime? EventCutoff { get; private set; }

    public DateTime? SessionCutoff { get; private set; }

    public DateTime? AuditCutoff { get; private set; }

    public int? BatchSize { get; private set; }

    public int ActivityCalls { get; private set; }

    public int EventCalls { get; private set; }

    public int AttemptCalls { get; private set; }

    public int SessionCalls { get; private set; }

    public int AuditCalls { get; private set; }

    public int VacuumCalls { get; private set; }

    public bool ThrowOnVacuum { get; set; }

    public Task<int> DeleteActivitySamplesBeforeAsync(
        DateTime cutoffUtc,
        int batchSize,
        CancellationToken cancellationToken = default)
    {
        ActivityCutoff = cutoffUtc;
        BatchSize = batchSize;
        ActivityCalls++;

        if (FailActivityCall.Contains(ActivityCalls))
            throw new InvalidOperationException("The activity sample delete failed.");

        return Task.FromResult(Next(ActivityBatches));
    }

    public Task<int> DeleteAttentionEventsBeforeAsync(
        DateTime cutoffUtc,
        int batchSize,
        CancellationToken cancellationToken = default)
    {
        EventCutoff = cutoffUtc;
        BatchSize = batchSize;
        EventCalls++;
        return Task.FromResult(Next(EventBatches));
    }

    public Task<int> DeleteBlockedAttemptsBeforeAsync(
        DateTime cutoffUtc,
        int batchSize,
        CancellationToken cancellationToken = default)
    {
        EventCutoff = cutoffUtc;
        BatchSize = batchSize;
        AttemptCalls++;
        return Task.FromResult(Next(AttemptBatches));
    }

    public Task<int> DeleteFinishedSessionsBeforeAsync(
        DateTime cutoffUtc,
        int batchSize,
        CancellationToken cancellationToken = default)
    {
        SessionCutoff = cutoffUtc;
        BatchSize = batchSize;
        SessionCalls++;
        return Task.FromResult(Next(SessionBatches));
    }

    public Task<int> DeleteAuditLogsBeforeAsync(
        DateTime cutoffUtc,
        int batchSize,
        CancellationToken cancellationToken = default)
    {
        AuditCutoff = cutoffUtc;
        BatchSize = batchSize;
        AuditCalls++;
        return Task.FromResult(Next(AuditBatches));
    }

    public Task VacuumAsync(CancellationToken cancellationToken = default)
    {
        VacuumCalls++;

        if (ThrowOnVacuum)
            throw new InvalidOperationException("The vacuum failed.");

        return Task.CompletedTask;
    }

    private static int Next(Queue<int> batches) => batches.Count == 0 ? 0 : batches.Dequeue();
}
