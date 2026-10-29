using EduFocus.Domain.Entities;
using EduFocus.Domain.ValueObjects;
using EduFocus.Ports.Outbound.Persistence;

namespace EduFocus.UseCases.Tests.TestSupport;

internal sealed class FakeAttentionEventRepository : IAttentionEventRepository
{
    public List<AttentionEvent> Events { get; } = [];

    public bool ThrowOnAdd { get; set; }

    public bool ThrowOnUpdate { get; set; }

    public bool ThrowOnCloseOpen { get; set; }

    public int AddCalls { get; private set; }

    public int UpdateCalls { get; private set; }

    public List<DateTime> CloseOpenCalls { get; } = [];

    public Task AddAsync(AttentionEvent attentionEvent, CancellationToken cancellationToken = default)
    {
        AddCalls++;

        if (ThrowOnAdd)
            throw new InvalidOperationException("The attention event store is unavailable.");

        Events.Add(attentionEvent);
        return Task.CompletedTask;
    }

    public Task UpdateAsync(AttentionEvent attentionEvent, CancellationToken cancellationToken = default)
    {
        UpdateCalls++;

        if (ThrowOnUpdate)
            throw new InvalidOperationException("The attention event store is unavailable.");

        if (!Events.Contains(attentionEvent))
            Events.Add(attentionEvent);

        return Task.CompletedTask;
    }

    public Task<AttentionEvent?> GetOpenAsync(
        StudySessionId sessionId,
        CancellationToken cancellationToken = default) =>
        Task.FromResult(Events
            .Where(x => x.SessionId == sessionId && x.IsOpen)
            .OrderByDescending(x => x.StartedAtUtc)
            .FirstOrDefault());

    public Task<IReadOnlyList<AttentionEvent>> ListBySessionAsync(
        StudySessionId sessionId,
        CancellationToken cancellationToken = default) =>
        Task.FromResult<IReadOnlyList<AttentionEvent>>(
            Events
                .Where(x => x.SessionId == sessionId)
                .OrderBy(x => x.StartedAtUtc)
                .ToList());

    public Task<int> CloseOpenAsync(
        StudySessionId sessionId,
        DateTime endedAtUtc,
        CancellationToken cancellationToken = default)
    {
        CloseOpenCalls.Add(endedAtUtc);

        if (ThrowOnCloseOpen)
            throw new InvalidOperationException("The attention event store is unavailable.");

        var open = Events
            .Where(x => x.SessionId == sessionId && x.IsOpen)
            .ToList();

        foreach (var attentionEvent in open)
        {
            var closeAt = endedAtUtc < attentionEvent.StartedAtUtc
                ? attentionEvent.StartedAtUtc
                : endedAtUtc;
            attentionEvent.Close(closeAt);
        }

        return Task.FromResult(open.Count);
    }
}
