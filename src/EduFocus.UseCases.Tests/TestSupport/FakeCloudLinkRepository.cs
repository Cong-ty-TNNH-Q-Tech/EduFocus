using EduFocus.Domain.Entities;
using EduFocus.Ports.Outbound.Persistence;

namespace EduFocus.UseCases.Tests.TestSupport;

internal sealed class FakeCloudLinkRepository : ICloudLinkRepository
{
    public CloudLink? Stored { get; set; }

    public int AddCount { get; private set; }

    public int UpdateCount { get; private set; }

    public bool ThrowOnGet { get; set; }

    public Task<CloudLink?> GetAsync(CancellationToken cancellationToken = default) =>
        ThrowOnGet
            ? throw new InvalidOperationException("Storage unavailable.")
            : Task.FromResult(Stored);

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

    public Task<bool> TryRecordSyncAsync(
        string desktopToken,
        DateTime lastSyncAtUtc,
        DateTime updatedAtUtc,
        CancellationToken cancellationToken = default)
    {
        if (Stored is null || !Stored.IsLinked || Stored.DesktopToken != desktopToken)
            return Task.FromResult(false);

        Stored.RecordSync(lastSyncAtUtc, Max(updatedAtUtc, Stored.UpdatedAtUtc));
        return Task.FromResult(true);
    }

    public Task<bool> TryRecordErrorAsync(
        string desktopToken,
        string? message,
        DateTime updatedAtUtc,
        CancellationToken cancellationToken = default)
    {
        if (Stored is null || !Stored.IsEnabled || Stored.DesktopToken != desktopToken)
            return Task.FromResult(false);

        Stored.RecordError(message, Max(updatedAtUtc, Stored.UpdatedAtUtc));
        return Task.FromResult(true);
    }

    private static DateTime Max(DateTime left, DateTime right) => left >= right ? left : right;
}
