using EduFocus.Domain.Entities;
using EduFocus.Ports.DTOs;
using EduFocus.Ports.Outbound.Persistence;

namespace EduFocus.UseCases.Tests.TestSupport;

internal sealed class FakeCloudOutboxRepository : ICloudOutboxRepository
{
    public List<CloudOutboxEvent> Items { get; } = [];

    public CloudOutboxStatus Status { get; set; } = new(0, 0, null, null);

    public int TrimCalls { get; private set; }

    public int TrimRemoved { get; set; }

    public Task AddAsync(CloudOutboxEvent item, CancellationToken cancellationToken = default)
    {
        Items.Add(item);
        return Task.CompletedTask;
    }

    public Task<bool> ExistsAsync(string clientEventId, CancellationToken cancellationToken = default) =>
        Task.FromResult(Items.Exists(item =>
            string.Equals(item.ClientEventId, clientEventId, StringComparison.Ordinal)));

    public Task<IReadOnlyList<CloudOutboxEvent>> GetDueAsync(
        DateTime utcNow,
        int limit,
        CancellationToken cancellationToken = default) =>
        Task.FromResult<IReadOnlyList<CloudOutboxEvent>>([]);

    public Task MarkSentAsync(
        IReadOnlyList<Guid> ids,
        DateTime sentAtUtc,
        CancellationToken cancellationToken = default) =>
        Task.CompletedTask;

    public Task ScheduleRetryAsync(
        IReadOnlyList<Guid> ids,
        DateTime nextAttemptAtUtc,
        string? lastError,
        CancellationToken cancellationToken = default) =>
        Task.CompletedTask;

    public Task MarkFailedAsync(
        IReadOnlyList<Guid> ids,
        DateTime failedAtUtc,
        string? reason,
        CancellationToken cancellationToken = default) =>
        Task.CompletedTask;

    public Task<int> TrimAsync(
        DateTime utcNow,
        int maxPendingEvents,
        TimeSpan maxAge,
        CancellationToken cancellationToken = default)
    {
        TrimCalls++;
        return Task.FromResult(TrimRemoved);
    }

    public Task<CloudOutboxStatus> GetStatusAsync(CancellationToken cancellationToken = default) =>
        Task.FromResult(Status);
}
