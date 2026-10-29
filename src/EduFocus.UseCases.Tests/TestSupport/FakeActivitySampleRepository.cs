using EduFocus.Domain.Entities;
using EduFocus.Ports.Outbound.Persistence;

namespace EduFocus.UseCases.Tests.TestSupport;

internal sealed class FakeActivitySampleRepository : IActivitySampleRepository
{
    public List<ActivitySample> Samples { get; } = [];

    public bool ThrowOnAdd { get; set; }

    public Task AddAsync(ActivitySample sample, CancellationToken cancellationToken = default)
    {
        if (ThrowOnAdd)
            throw new InvalidOperationException("The sample store is unavailable.");

        Samples.Add(sample);
        return Task.CompletedTask;
    }
}
