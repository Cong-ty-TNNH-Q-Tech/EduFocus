using EduFocus.Domain.Enums;
using EduFocus.Ports.Outbound.Cloud;
using EduFocus.Ports.Security;
using EduFocus.UseCases.Tests.TestSupport;
using FluentAssertions;

namespace EduFocus.UseCases.Tests.Cloud;

public sealed class CloudLinkServiceTests
{
    [Fact]
    public async Task Status_is_local_and_mutations_require_parent_mode()
    {
        var harness = CloudLinkHarness.Create(authenticated: false);

        var snapshot = await harness.Service.GetStatusAsync();

        snapshot.IsEnabled.Should().BeFalse();
        snapshot.HasConsent.Should().BeFalse();

        var consent = () => harness.Service.AcceptConsentAsync();
        await consent.Should().ThrowAsync<ParentAuthorizationException>();

        var enable = EnableAsync(harness.Service);
        await enable.Should().ThrowAsync<ParentAuthorizationException>();

        harness.Sync.Calls.Should().BeEmpty();
    }

    [Fact]
    public async Task Enable_without_consent_is_rejected_and_makes_no_network_call()
    {
        var harness = CloudLinkHarness.Create();

        var enable = EnableAsync(harness.Service);

        await enable.Should().ThrowAsync<InvalidOperationException>()
            .WithMessage("*đồng ý*");
        harness.Sync.Calls.Should().BeEmpty();
        harness.Links.Stored.Should().BeNull();
    }

    [Fact]
    public async Task Enable_rejects_an_http_url_for_a_public_host()
    {
        var harness = CloudLinkHarness.Create();
        await harness.AcceptConsentAsync();

        var enable = EnableAsync(harness.Service, "http://cloud.example.com");

        await enable.Should().ThrowAsync<ArgumentException>();
        harness.Sync.Calls.Should().BeEmpty();
        harness.Links.Stored!.IsEnabled.Should().BeFalse();
    }

    [Fact]
    public async Task Enable_creates_a_pairing_code_and_keeps_the_token_out_of_audits_and_status()
    {
        var harness = CloudLinkHarness.Create();
        await harness.AcceptConsentAsync();

        var info = await harness.Service.EnableAsync(CloudLinkHarness.Options());

        info.SessionId.Should().Be(FakeCloudSyncPort.SessionId);
        info.Code.Should().Be(FakeCloudSyncPort.PairingCode);
        info.QrPayload.Should().StartWith("edufocus://pair?v=1&");
        info.ExpiresAtUtc.Should().Be(new DateTime(2026, 11, 17, 9, 10, 0, DateTimeKind.Utc));

        harness.Sync.CreateRequests.Should().ContainSingle()
            .Which.TimeZoneId.Should().Be("Asia/Ho_Chi_Minh");
        harness.Sync.CreateRequests[0].DesktopName.Should().Be("PC của An");

        var link = harness.Links.Stored!;
        link.IsEnabled.Should().BeTrue();
        link.HasConsent.Should().BeTrue();
        link.DesktopToken.Should().Be(FakeCloudSyncPort.DesktopToken);
        link.HasActivePairing(CloudLinkHarness.Start.UtcDateTime).Should().BeTrue();

        harness.Audits.Entries.Select(entry => entry.EventType)
            .Should().BeEquivalentTo(
                [AuditEventType.CloudConsentRecorded, AuditEventType.CloudPairingCodeCreated]);

        var auditText = string.Join(
            "\n",
            harness.Audits.Entries.Select(entry => $"{entry.Description}\n{entry.MetadataJson}"));
        auditText.Should().NotContain(FakeCloudSyncPort.DesktopToken);
        auditText.Should().NotContain(FakeCloudSyncPort.PairingCode);

        var logText = string.Join("\n", harness.Logger.Warnings.Concat(harness.Logger.Errors));
        logText.Should().NotContain(FakeCloudSyncPort.DesktopToken);

        var snapshot = await harness.Service.GetStatusAsync();
        snapshot.ActivePairing.Should().NotBeNull();
        snapshot.ActivePairing!.Code.Should().Be(FakeCloudSyncPort.PairingCode);
        snapshot.ToString().Should().NotContain(FakeCloudSyncPort.DesktopToken);
    }

    [Fact]
    public async Task Enable_rejects_a_second_pairing_session_while_the_first_is_active()
    {
        var harness = CloudLinkHarness.Create();
        await harness.AcceptConsentAsync();
        await harness.EnableAsync();

        var enable = EnableAsync(harness.Service);

        await enable.Should().ThrowAsync<InvalidOperationException>()
            .WithMessage("*phiên ghép nối*");
        harness.Sync.Calls.Count(call => call == "create-pairing").Should().Be(1);
    }

    [Fact]
    public async Task Status_does_not_touch_the_network_while_the_cloud_is_disabled()
    {
        var harness = CloudLinkHarness.Create();
        await harness.AcceptConsentAsync();

        var snapshot = await harness.Service.GetStatusAsync();

        snapshot.IsEnabled.Should().BeFalse();
        snapshot.HasConsent.Should().BeTrue();
        snapshot.RemoteStatusChecked.Should().BeFalse();
        harness.Sync.Calls.Should().BeEmpty();
    }

    [Fact]
    public async Task Status_completes_the_link_when_the_phone_claims_the_code()
    {
        var harness = CloudLinkHarness.Create();
        await harness.AcceptConsentAsync();
        await harness.EnableAsync();

        harness.Sync.PairingStatus = FakeCloudSyncPort.CreateClaimedStatus();
        harness.Sync.RemoteStatus = FakeCloudSyncPort.CreateRemoteStatus();

        var snapshot = await harness.Service.GetStatusAsync();

        snapshot.IsEnabled.Should().BeTrue();
        snapshot.IsLinked.Should().BeTrue();
        snapshot.FamilyId.Should().Be(FakeCloudSyncPort.FamilyId);
        snapshot.DesktopDeviceId.Should().Be(FakeCloudSyncPort.DesktopDeviceId);
        snapshot.LinkedAtUtc.Should().Be(new DateTime(2026, 11, 17, 9, 0, 20, DateTimeKind.Utc));
        snapshot.LastSyncAtUtc.Should().Be(new DateTime(2026, 11, 17, 9, 0, 30, DateTimeKind.Utc));
        snapshot.Devices.Should().HaveCount(2);
        snapshot.PendingSyncEvents.Should().Be(2);
        snapshot.ActivePairing.Should().BeNull();
        snapshot.ToString().Should().NotContain(FakeCloudSyncPort.DesktopToken);

        harness.Audits.Entries.Should().Contain(
            entry => entry.EventType == AuditEventType.CloudLinkEnabled);

        var link = harness.Links.Stored!;
        link.DesktopToken.Should().Be(FakeCloudSyncPort.DesktopToken);
        link.PairingSessionId.Should().BeNull();
        link.PairingCode.Should().BeNull();
    }

    [Fact]
    public async Task Status_disables_the_link_when_the_pairing_code_expires()
    {
        var harness = CloudLinkHarness.Create();
        await harness.AcceptConsentAsync();
        await harness.EnableAsync();

        harness.Clock.Advance(TimeSpan.FromMinutes(11));
        harness.Sync.PairingStatus = FakeCloudSyncPort.CreatePendingStatus()
            with { Status = CloudPairingStatus.Expired };

        var snapshot = await harness.Service.GetStatusAsync();

        snapshot.IsEnabled.Should().BeFalse();
        snapshot.IsLinked.Should().BeFalse();
        harness.Links.Stored!.DesktopToken.Should().BeNull();
        harness.Links.Stored.HasConsent.Should().BeTrue();
    }

    [Fact]
    public async Task Status_reports_a_safe_error_when_the_cloud_is_unreachable()
    {
        var harness = CloudLinkHarness.Create();
        await harness.SeedLinkedAsync();

        harness.Sync.LinkStatusFailure = new CloudSyncException(
            CloudErrorCodes.Network,
            "Không kết nối được tới máy chủ cloud.",
            isNetworkError: true);

        var snapshot = await harness.Service.GetStatusAsync();

        snapshot.IsEnabled.Should().BeTrue();
        snapshot.IsLinked.Should().BeTrue();
        snapshot.LastError.Should().Be("Không kết nối được tới máy chủ cloud.");
        harness.Logger.Warnings.Should().Contain(message => message.Contains(CloudErrorCodes.Network));
        string.Join("\n", harness.Logger.Warnings)
            .Should().NotContain(FakeCloudSyncPort.DesktopToken);
    }

    [Fact]
    public async Task Status_clears_the_local_link_when_the_server_rejects_the_token()
    {
        var harness = CloudLinkHarness.Create();
        await harness.SeedLinkedAsync();

        harness.Sync.LinkStatusFailure = new CloudSyncException(
            CloudErrorCodes.Unauthorized,
            "Máy chủ cloud từ chối token của thiết bị này.",
            statusCode: 401);

        var snapshot = await harness.Service.GetStatusAsync();

        snapshot.IsEnabled.Should().BeFalse();
        snapshot.IsLinked.Should().BeFalse();
        snapshot.LastError.Should().NotBeNullOrWhiteSpace();

        var link = harness.Links.Stored!;
        link.DesktopToken.Should().BeNull();
        link.IsEnabled.Should().BeFalse();
        link.HasConsent.Should().BeTrue();

        harness.Audits.Entries.Should().Contain(
            entry => entry.EventType == AuditEventType.CloudLinkRevoked
                && entry.Actor == AuditActor.System);
    }

    [Fact]
    public async Task Unlink_revokes_the_device_and_stays_unlinked_after_a_restart()
    {
        var harness = CloudLinkHarness.Create();
        await harness.SeedLinkedAsync();

        await harness.Service.UnlinkAsync(deleteCloudData: false);

        harness.Sync.RevokedDevices.Should().ContainSingle();
        harness.Sync.RevokedDevices[0].DeviceId.Should().Be(FakeCloudSyncPort.DesktopDeviceId);
        harness.Sync.RevokedDevices[0].Token.Should().Be(FakeCloudSyncPort.DesktopToken);
        harness.Sync.DeleteFamilyCalls.Should().Be(0);

        var link = harness.Links.Stored!;
        link.IsEnabled.Should().BeFalse();
        link.DesktopToken.Should().BeNull();
        link.FamilyId.Should().BeNull();
        link.DesktopDeviceId.Should().BeNull();
        link.HasConsent.Should().BeTrue();

        harness.Audits.Entries.Should().Contain(
            entry => entry.EventType == AuditEventType.CloudLinkRevoked);

        var networkCallsBeforeRestart = harness.Sync.Calls.Count;
        var reopened = harness.CreateService();
        var snapshot = await reopened.GetStatusAsync();

        snapshot.IsEnabled.Should().BeFalse();
        snapshot.IsLinked.Should().BeFalse();
        harness.Sync.Calls.Should().HaveCount(networkCallsBeforeRestart);
    }

    [Fact]
    public async Task Unlink_with_delete_cloud_data_calls_the_family_delete()
    {
        var harness = CloudLinkHarness.Create();
        await harness.SeedLinkedAsync();

        await harness.Service.UnlinkAsync(deleteCloudData: true);

        harness.Sync.DeleteFamilyCalls.Should().Be(1);
        harness.Sync.RevokedDevices.Should().BeEmpty();
        harness.Audits.Entries.Should().Contain(
            entry => entry.EventType == AuditEventType.CloudDataDeletionRequested);

        var link = harness.Links.Stored!;
        link.DesktopToken.Should().BeNull();
        link.FamilyId.Should().BeNull();
        link.IsEnabled.Should().BeFalse();
        link.HasConsent.Should().BeTrue();
    }

    [Fact]
    public async Task Unlink_clears_local_state_even_when_the_server_is_unreachable()
    {
        var harness = CloudLinkHarness.Create();
        await harness.SeedLinkedAsync();
        harness.Sync.UnlinkFailure = new CloudSyncException(
            CloudErrorCodes.Network,
            "Không kết nối được tới máy chủ cloud.",
            isNetworkError: true);

        var unlink = async () => await harness.Service.UnlinkAsync(deleteCloudData: false);
        var exception = (await unlink.Should().ThrowAsync<CloudSyncException>()).Which;

        exception.IsNetworkError.Should().BeTrue();
        exception.Message.Should().Contain("Đã ngắt liên kết");

        var link = harness.Links.Stored!;
        link.DesktopToken.Should().BeNull();
        link.IsEnabled.Should().BeFalse();
        link.LastError.Should().NotBeNullOrWhiteSpace();

        harness.Audits.Entries.Should().Contain(
            entry => entry.EventType == AuditEventType.CloudLinkRevoked);
        harness.Logger.Warnings.Should().Contain(
            message => message.Contains(CloudErrorCodes.Network));
    }

    [Fact]
    public async Task Revoke_consent_unlinks_and_clears_the_consent_timestamp()
    {
        var harness = CloudLinkHarness.Create();
        await harness.SeedLinkedAsync();

        await harness.Service.RevokeConsentAsync();

        var link = harness.Links.Stored!;
        link.HasConsent.Should().BeFalse();
        link.ConsentAcceptedAtUtc.Should().BeNull();
        link.IsEnabled.Should().BeFalse();
        link.DesktopToken.Should().BeNull();

        harness.Sync.RevokedDevices.Should().ContainSingle();
        harness.Audits.Entries.Select(entry => entry.EventType)
            .Should().Contain(AuditEventType.CloudLinkRevoked)
            .And.Contain(AuditEventType.CloudConsentRevoked);
    }

    [Fact]
    public async Task Token_never_appears_in_logs_audits_or_status_snapshots()
    {
        var harness = CloudLinkHarness.Create();
        await harness.SeedLinkedAsync();

        harness.Sync.LinkStatusFailure = new CloudSyncException(
            CloudErrorCodes.Network,
            "Không kết nối được tới máy chủ cloud.");
        var snapshot = await harness.Service.GetStatusAsync();

        harness.Sync.LinkStatusFailure = null;
        harness.Sync.UnlinkFailure = new CloudSyncException(
            CloudErrorCodes.Network,
            "Không kết nối được tới máy chủ cloud.",
            isNetworkError: true);
        var unlink = async () => await harness.Service.UnlinkAsync(deleteCloudData: true);
        await unlink.Should().ThrowAsync<CloudSyncException>();

        var text = string.Join(
                "\n",
                harness.Audits.Entries.Select(entry => $"{entry.Description}\n{entry.MetadataJson}"))
            + string.Join("\n", harness.Logger.Warnings)
            + snapshot.ToString();

        text.Should().NotContain(FakeCloudSyncPort.DesktopToken);
        text.Should().NotContain(FakeCloudSyncPort.PairingCode);
    }

    private static Func<Task> EnableAsync(
        EduFocus.UseCases.Cloud.CloudLinkService service,
        string cloudBaseUrl = "https://cloud.example.com") =>
        async () => await service.EnableAsync(CloudLinkHarness.Options(cloudBaseUrl));
}
