using EduFocus.Ports.Outbound.Cloud;

namespace EduFocus.UseCases.Tests.TestSupport;

internal sealed class FakeCloudSyncPort : ICloudSyncPort
{
    public static readonly Guid SessionId = Guid.Parse("0f8fad5b-d9cb-469f-a165-70867728950e");
    public static readonly Guid FamilyId = Guid.Parse("7c9e6679-7425-40de-944b-e07fc1f90ae7");
    public static readonly Guid DesktopDeviceId = Guid.Parse("3f2504e0-4f89-41d3-9a0c-0305e82c3301");
    public static readonly Guid MobileDeviceId = Guid.Parse("9b2d4a3e-6c1f-4d5a-8b7c-2e1f0a9d8c7b");

    public const string DesktopToken = "efd_desktop_token_must_never_leak";
    public const string PairingCode = "ABCD2345";

    public List<string> Calls { get; } = [];

    public List<CloudPairingRequest> CreateRequests { get; } = [];

    public List<(Guid DeviceId, string Token)> RevokedDevices { get; } = [];

    public int DeleteFamilyCalls { get; private set; }

    public CloudPairingSession Session { get; set; } = CreatePendingSession();

    public CloudPairingSessionStatus PairingStatus { get; set; } = CreatePendingStatus();

    public CloudRemoteLinkStatus RemoteStatus { get; set; } = CreateRemoteStatus();

    public CloudSyncException? CreateFailure { get; set; }

    public CloudSyncException? PairingStatusFailure { get; set; }

    public CloudSyncException? LinkStatusFailure { get; set; }

    public CloudSyncException? UnlinkFailure { get; set; }

    public Task<CloudPairingSession> CreatePairingSessionAsync(
        Uri cloudBaseUrl,
        CloudPairingRequest request,
        CancellationToken cancellationToken = default)
    {
        Calls.Add("create-pairing");
        CreateRequests.Add(request);

        if (CreateFailure is not null)
            throw CreateFailure;

        return Task.FromResult(Session);
    }

    public Task<CloudPairingSessionStatus> GetPairingSessionStatusAsync(
        Uri cloudBaseUrl,
        Guid sessionId,
        string desktopToken,
        CancellationToken cancellationToken = default)
    {
        Calls.Add("pairing-status");

        if (PairingStatusFailure is not null)
            throw PairingStatusFailure;

        return Task.FromResult(PairingStatus);
    }

    public Task<CloudRemoteLinkStatus> GetLinkStatusAsync(
        Uri cloudBaseUrl,
        string desktopToken,
        CancellationToken cancellationToken = default)
    {
        Calls.Add("link-status");

        if (LinkStatusFailure is not null)
            throw LinkStatusFailure;

        return Task.FromResult(RemoteStatus);
    }

    public Task RevokeDeviceAsync(
        Uri cloudBaseUrl,
        string desktopToken,
        Guid deviceId,
        CancellationToken cancellationToken = default)
    {
        Calls.Add("revoke-device");
        RevokedDevices.Add((deviceId, desktopToken));

        if (UnlinkFailure is not null)
            throw UnlinkFailure;

        return Task.CompletedTask;
    }

    public Task DeleteFamilyAsync(
        Uri cloudBaseUrl,
        string desktopToken,
        CancellationToken cancellationToken = default)
    {
        Calls.Add("delete-family");
        DeleteFamilyCalls++;

        if (UnlinkFailure is not null)
            throw UnlinkFailure;

        return Task.CompletedTask;
    }

    public static CloudPairingSession CreatePendingSession() =>
        new(
            SessionId,
            PairingCode,
            $"edufocus://pair?v=1&u=Y2xvdWQ&s={SessionId:D}&c={PairingCode}",
            new DateTime(2026, 11, 17, 9, 10, 0, DateTimeKind.Utc),
            DesktopToken,
            "https://cloud.example.com");

    public static CloudPairingSessionStatus CreatePendingStatus() =>
        new(
            SessionId,
            CloudPairingStatus.Pending,
            new DateTime(2026, 11, 17, 9, 10, 0, DateTimeKind.Utc),
            null,
            null,
            null);

    public static CloudPairingSessionStatus CreateClaimedStatus() =>
        new(
            SessionId,
            CloudPairingStatus.Claimed,
            new DateTime(2026, 11, 17, 9, 10, 0, DateTimeKind.Utc),
            new DateTime(2026, 11, 17, 9, 0, 20, DateTimeKind.Utc),
            "Điện thoại của mẹ",
            MobileDeviceId);

    public static CloudRemoteLinkStatus CreateRemoteStatus() =>
        new(
            true,
            FamilyId,
            DesktopDeviceId,
            "Asia/Ho_Chi_Minh",
            new DateTime(2026, 11, 17, 9, 0, 0, DateTimeKind.Utc),
            new DateTime(2026, 11, 17, 9, 0, 30, DateTimeKind.Utc),
            2,
            [
                new CloudRemoteDevice(
                    DesktopDeviceId,
                    "Desktop",
                    "PC của An",
                    "windows",
                    new DateTime(2026, 11, 17, 9, 0, 0, DateTimeKind.Utc),
                    new DateTime(2026, 11, 17, 9, 0, 30, DateTimeKind.Utc),
                    false),
                new CloudRemoteDevice(
                    MobileDeviceId,
                    "Mobile",
                    "Điện thoại của mẹ",
                    "android",
                    new DateTime(2026, 11, 17, 9, 0, 20, DateTimeKind.Utc),
                    new DateTime(2026, 11, 17, 9, 0, 25, DateTimeKind.Utc),
                    false)
            ]);
}
