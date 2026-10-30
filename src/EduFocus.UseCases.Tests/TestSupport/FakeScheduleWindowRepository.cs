using EduFocus.Domain.Entities;
using EduFocus.Domain.ValueObjects;
using EduFocus.Ports.Outbound.Persistence;

namespace EduFocus.UseCases.Tests.TestSupport;

internal sealed class FakeScheduleWindowRepository : IScheduleWindowRepository
{
    public List<ScheduleWindow> Windows { get; } = [];

    public Task<IReadOnlyList<ScheduleWindow>> ListAsync(CancellationToken cancellationToken = default) =>
        Task.FromResult<IReadOnlyList<ScheduleWindow>>(Windows);

    public Task<ScheduleWindow?> GetAsync(
        ScheduleWindowId id,
        CancellationToken cancellationToken = default) =>
        Task.FromResult(Windows.SingleOrDefault(window => window.Id == id));

    public Task AddAsync(ScheduleWindow window, CancellationToken cancellationToken = default)
    {
        Windows.Add(window);
        return Task.CompletedTask;
    }

    public Task UpdateAsync(ScheduleWindow window, CancellationToken cancellationToken = default)
    {
        if (!Windows.Contains(window))
            Windows.Add(window);

        return Task.CompletedTask;
    }

    public Task DeleteAsync(ScheduleWindowId id, CancellationToken cancellationToken = default)
    {
        Windows.RemoveAll(window => window.Id == id);
        return Task.CompletedTask;
    }
}
