using EduFocus.Domain.Entities;
using EduFocus.Ports.Outbound.Persistence;

namespace EduFocus.UseCases.Tests.TestSupport;

internal sealed class FakeCloudLinkRepository : ICloudLinkRepository
{
    public CloudLink? Stored { get; set; }

    public int AddCount { get; private set; }

    public int UpdateCount { get; private set; }

    public Task<CloudLink?> GetAsync(CancellationToken cancellationToken = default) =>
        Task.FromResult(Stored);

    public Task AddAsync(CloudLink link, CancellationToken cancellationToken = default)
    {
        AddCount++;
        Stored = link;
        return Task.CompletedTask;
    }

    public Task UpdateAsync(CloudLink link, CancellationToken cancellationToken = default)
    {
        UpdateCount++;
        Stored = link;
        return Task.CompletedTask;
    }
}
