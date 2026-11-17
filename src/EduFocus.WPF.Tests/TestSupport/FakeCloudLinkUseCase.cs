using EduFocus.Ports.DTOs;
using EduFocus.Ports.Inbound;

namespace EduFocus.WPF.Tests.TestSupport;

internal sealed class FakeCloudLinkUseCase : ICloudLinkUseCase
{
    public CloudLinkStatusSnapshot Snapshot { get; set; } = CloudTestData.Empty();

    public CloudPairingInfo? Pairing { get; set; }

    public int GetStatusCalls { get; private set; }

    public int AcceptConsentCalls { get; private set; }

    public int EnableCalls { get; private set; }

    public int UnlinkCalls { get; private set; }

    public bool? LastDeleteCloudData { get; private set; }

    public CloudLinkOptions? LastOptions { get; private set; }

    public Exception? ThrowOnEnable { get; set; }

    public Exception? ThrowOnUnlink { get; set; }

    public Task<CloudLinkStatusSnapshot> GetStatusAsync(CancellationToken cancellationToken = default)
    {
        GetStatusCalls++;
        return Task.FromResult(Snapshot);
    }

    public Task AcceptConsentAsync(CancellationToken cancellationToken = default)
    {
        AcceptConsentCalls++;
        Snapshot = Snapshot with
        {
            HasConsent = true,
            ConsentAcceptedAtUtc = DateTime.UtcNow
        };

        return Task.CompletedTask;
    }

    public Task RevokeConsentAsync(CancellationToken cancellationToken = default) =>
        Task.CompletedTask;

    public Task<CloudPairingInfo> EnableAsync(
        CloudLinkOptions options,
        CancellationToken cancellationToken = default)
    {
        EnableCalls++;
        LastOptions = options;

        if (ThrowOnEnable is not null)
            throw ThrowOnEnable;

        var pairing = Pairing ?? new CloudPairingInfo(
            Guid.NewGuid(),
            "ABCD2345",
            DateTime.UtcNow.AddMinutes(10),
            "edufocus://pair?v=1&s=session&c=ABCD2345",
            options.CloudBaseUrl);

        Snapshot = Snapshot with
        {
            IsEnabled = true,
            HasConsent = true,
            CloudBaseUrl = options.CloudBaseUrl,
            ActivePairing = pairing
        };

        return Task.FromResult(pairing);
    }

    public Task UnlinkAsync(bool deleteCloudData, CancellationToken cancellationToken = default)
    {
        UnlinkCalls++;
        LastDeleteCloudData = deleteCloudData;

        // The real service clears local state even when the server call fails.
        Snapshot = CloudTestData.Disabled(hasConsent: true);

        if (ThrowOnUnlink is not null)
            throw ThrowOnUnlink;

        return Task.CompletedTask;
    }
}
