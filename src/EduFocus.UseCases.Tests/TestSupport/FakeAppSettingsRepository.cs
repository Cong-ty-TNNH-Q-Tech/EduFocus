using EduFocus.Domain.Entities;
using EduFocus.Ports.Outbound.Persistence;

namespace EduFocus.UseCases.Tests.TestSupport;

internal sealed class FakeAppSettingsRepository : IAppSettingsRepository
{
    public AppSettings? Stored { get; set; }

    public int AddCount { get; private set; }

    public int UpdateCount { get; private set; }

    public bool ThrowOnUpdate { get; set; }

    public bool ThrowOnRead { get; set; }

    public Task<AppSettings?> GetAsync(CancellationToken cancellationToken = default)
    {
        if (ThrowOnRead)
            throw new InvalidOperationException("The settings store is unavailable.");

        return Task.FromResult(Stored);
    }

    public Task AddAsync(AppSettings settings, CancellationToken cancellationToken = default)
    {
        AddCount++;
        Stored = settings;
        return Task.CompletedTask;
    }

    public Task UpdateAsync(AppSettings settings, CancellationToken cancellationToken = default)
    {
        if (ThrowOnUpdate)
            throw new InvalidOperationException("The settings store is unavailable.");

        UpdateCount++;
        Stored = settings;
        return Task.CompletedTask;
    }
}
