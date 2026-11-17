using EduFocus.Ports.DTOs;
using EduFocus.UseCases.Auth;
using EduFocus.UseCases.Cloud;

namespace EduFocus.UseCases.Tests.TestSupport;

internal sealed class CloudLinkHarness
{
    public static readonly DateTimeOffset Start = new(2026, 11, 17, 9, 0, 0, TimeSpan.Zero);

    private CloudLinkHarness(bool authenticated)
    {
        Clock = new ManualTimeProvider(Start, TimeZoneInfo.Utc);
        var session = new ParentSessionState(Clock);
        if (authenticated)
            session.Authenticate();

        Session = session;
        Service = CreateService();
    }

    public static CloudLinkHarness Create(bool authenticated = true) => new(authenticated);

    public ManualTimeProvider Clock { get; }

    public ParentSessionState Session { get; }

    public FakeCloudLinkRepository Links { get; } = new();

    public FakeCloudSyncPort Sync { get; } = new();

    public RecordingAuditLogRepository Audits { get; } = new();

    public RecordingLogger Logger { get; } = new();

    public TestCorrelationContext Correlation { get; } = new();

    public CloudLinkService Service { get; private set; } = null!;

    public static CloudLinkOptions Options(string cloudBaseUrl = "https://cloud.example.com") =>
        new(cloudBaseUrl, "PC của An", "windows", "1.0.0", "Asia/Ho_Chi_Minh");

    /// <summary>Fresh service over the same persisted repository, like an app restart.</summary>
    public CloudLinkService CreateService()
    {
        Service = new CloudLinkService(
            Links,
            Sync,
            Session,
            Audits,
            Correlation,
            Logger,
            Clock);
        return Service;
    }

    public async Task AcceptConsentAsync()
    {
        await Service.AcceptConsentAsync();
    }

    public async Task EnableAsync(string cloudBaseUrl = "https://cloud.example.com")
    {
        await Service.EnableAsync(Options(cloudBaseUrl));
    }

    /// <summary>Enables, claims and completes pairing so tests start from a linked state.</summary>
    public async Task SeedLinkedAsync(DateTime? lastSyncAtUtc = null)
    {
        await AcceptConsentAsync();
        await EnableAsync();

        Sync.PairingStatus = FakeCloudSyncPort.CreateClaimedStatus();
        var remote = FakeCloudSyncPort.CreateRemoteStatus();
        if (lastSyncAtUtc is { } lastSync)
            remote = remote with { LastSyncAtUtc = lastSync };
        Sync.RemoteStatus = remote;

        await Service.GetStatusAsync();
    }
}
