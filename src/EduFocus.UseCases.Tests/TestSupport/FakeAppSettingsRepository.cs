using EduFocus.Domain.Entities;
using EduFocus.Ports.Outbound.Persistence;

namespace EduFocus.UseCases.Tests.TestSupport;

internal sealed class FakeAppSettingsRepository : IAppSettingsRepository
{
    public AppSettings? Stored { get; set; }

    public int AddCount { get; private set; }

    public int UpdateCount { get; private set; }

    public Task<AppSettings?> GetAsync(CancellationToken cancellationToken = default) =>
        Task.FromResult(Stored);

    public Task AddAsync(AppSettings settings, CancellationToken cancellationToken = default)
    {
        AddCount++;
        Stored = settings;
        return Task.CompletedTask;
    }

    public Task UpdateAsync(AppSettings settings, CancellationToken cancellationToken = default)
    {
        UpdateCount++;
        Stored = settings;
        return Task.CompletedTask;
    }
}
