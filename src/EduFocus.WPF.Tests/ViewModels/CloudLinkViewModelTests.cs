using EduFocus.Ports.Outbound.Cloud;
using EduFocus.WPF.Tests.TestSupport;
using EduFocus.WPF.ViewModels;
using FluentAssertions;

namespace EduFocus.WPF.Tests.ViewModels;

public sealed class CloudLinkViewModelTests
{
    private readonly FakeCloudLinkUseCase _links = new();
    private readonly FakeCloudOutboxNotifier _notifier = new();
    private readonly FakeClipboardService _clipboard = new();
    private readonly RecordingLogger _logger = new();
    private readonly ManualTimeProvider _time =
        new(new DateTimeOffset(2026, 11, 17, 3, 0, 0, TimeSpan.Zero));

    [Fact]
    public void Enable_requires_the_consent_checkbox()
    {
        var sut = CreateSut();

        sut.CanEnable.Should().BeFalse();

        sut.ConsentAccepted = true;
        sut.CanEnable.Should().BeTrue();

        sut.ConsentAccepted = false;
        sut.CanEnable.Should().BeFalse();
    }

    [Fact]
    public async Task Enable_without_consent_reports_an_error_and_calls_nothing()
    {
        var sut = CreateSut();

        await sut.EnableCommand.ExecuteAsync(null);

        _links.EnableCalls.Should().Be(0);
        _links.AcceptConsentCalls.Should().Be(0);
        sut.StatusIsError.Should().BeTrue();
        sut.ShowEnablePanel.Should().BeTrue();
    }

    [Fact]
    public async Task Enable_with_consent_records_consent_and_shows_the_pairing_code()
    {
        var sut = CreateSut();
        sut.ConsentAccepted = true;
        sut.CloudBaseUrl = CloudTestData.BaseUrl;
        sut.DesktopName = "PC của mẹ";

        await sut.EnableCommand.ExecuteAsync(null);

        _links.AcceptConsentCalls.Should().Be(1);
        _links.EnableCalls.Should().Be(1);
        _links.LastOptions!.CloudBaseUrl.Should().Be(CloudTestData.BaseUrl);
        _links.LastOptions.DesktopName.Should().Be("PC của mẹ");
        sut.IsCloudEnabled.Should().BeTrue();
        sut.ShowPairingPanel.Should().BeTrue();
        sut.PairingCode.Should().Be("ABCD2345");
        sut.HasActivePairing.Should().BeTrue();
        sut.QrImage.Should().NotBeNull();
        sut.StatusIsError.Should().BeFalse();
    }

    [Fact]
    public async Task Enable_reuses_consent_recorded_earlier()
    {
        _links.Snapshot = CloudTestData.Disabled(hasConsent: true);
        var sut = CreateSut();
        await sut.RefreshAsync();
        sut.CloudBaseUrl = CloudTestData.BaseUrl;

        sut.ConsentAccepted.Should().BeTrue();
        await sut.EnableCommand.ExecuteAsync(null);

        _links.AcceptConsentCalls.Should().Be(0);
        _links.EnableCalls.Should().Be(1);
    }

    [Fact]
    public async Task Enable_rejects_urls_the_cloud_policy_refuses()
    {
        var sut = CreateSut();
        sut.ConsentAccepted = true;
        sut.CloudBaseUrl = "ftp://cloud.example.test";

        await sut.EnableCommand.ExecuteAsync(null);

        _links.EnableCalls.Should().Be(0);
        sut.StatusIsError.Should().BeTrue();
        sut.IsCloudEnabled.Should().BeFalse();
    }

    [Fact]
    public void Insecure_url_warning_tracks_the_address()
    {
        var sut = CreateSut();

        sut.CloudBaseUrl = "http://192.168.1.10:8080";
        sut.IsInsecureUrl.Should().BeTrue();

        sut.CloudBaseUrl = "https://cloud.example.test";
        sut.IsInsecureUrl.Should().BeFalse();
    }

    [Fact]
    public async Task Polling_only_runs_while_the_card_is_open_and_pending()
    {
        var sessionId = Guid.NewGuid();
        _links.Snapshot = CloudTestData.Pending(
            _time.UtcNow.UtcDateTime.AddMinutes(10),
            sessionId);

        var sut = CreateSut();
        await sut.RefreshAsync();
        _links.GetStatusCalls.Should().Be(1);

        sut.HandleWindowOpened();
        sut.SetSectionVisible(false);
        _time.Advance(TimeSpan.FromSeconds(10));
        sut.HandleTick();
        _links.GetStatusCalls.Should().Be(1);

        sut.SetSectionVisible(true);
        sut.HandleTick();
        _links.GetStatusCalls.Should().Be(2);

        sut.HandleWindowClosed();
        _time.Advance(TimeSpan.FromSeconds(10));
        sut.HandleTick();
        _links.GetStatusCalls.Should().Be(2);
    }

    [Fact]
    public async Task Polling_picks_up_the_phone_without_reopening_the_window()
    {
        var sessionId = Guid.NewGuid();
        _links.Snapshot = CloudTestData.Pending(
            _time.UtcNow.UtcDateTime.AddMinutes(10),
            sessionId);

        var sut = CreateSut();
        await sut.RefreshAsync();
        sut.HandleWindowOpened();
        sut.SetSectionVisible(true);

        _links.Snapshot = CloudTestData.Linked(_time.UtcNow.UtcDateTime);
        sut.HandleTick();

        sut.IsLinked.Should().BeTrue();
        sut.ShowLinkedPanel.Should().BeTrue();
        sut.ShowPairingPanel.Should().BeFalse();
        sut.LinkedDeviceText.Should().Contain("Pixel của mẹ");
        sut.PendingEventsText.Should().Contain("2");
        sut.HasPairingMaterial.Should().BeFalse();

        var callsAfterLink = _links.GetStatusCalls;
        _time.Advance(TimeSpan.FromSeconds(30));
        sut.HandleTick();
        _links.GetStatusCalls.Should().Be(callsAfterLink);
    }

    [Fact]
    public async Task Pairing_countdown_expires_using_the_time_provider()
    {
        var sessionId = Guid.NewGuid();
        _links.Snapshot = CloudTestData.Pending(
            _time.UtcNow.UtcDateTime.AddMinutes(2),
            sessionId);

        var sut = CreateSut();
        await sut.RefreshAsync();
        sut.HandleWindowOpened();
        sut.SetSectionVisible(true);

        sut.PairingCountdown.Should().Contain("02:00");
        sut.HasActivePairing.Should().BeTrue();
        sut.IsPairingExpired.Should().BeFalse();
        sut.CanCreateNewPairing.Should().BeFalse();

        _time.Advance(TimeSpan.FromMinutes(2) + TimeSpan.FromSeconds(1));
        sut.HandleTick();

        sut.HasActivePairing.Should().BeFalse();
        sut.IsPairingExpired.Should().BeTrue();
        sut.CanCreateNewPairing.Should().BeTrue();
        sut.PairingCountdown.Should().Be("Mã ghép nối đã hết hạn.");
        sut.ShowPairingMaterial.Should().BeTrue();
        sut.ShowPairingPrompt.Should().BeFalse();
    }

    [Fact]
    public async Task Unlink_with_cloud_data_requires_the_typed_confirmation()
    {
        _links.Snapshot = CloudTestData.Linked(_time.UtcNow.UtcDateTime);
        var sut = CreateSut();
        await sut.RefreshAsync();
        sut.OpenUnlinkConfirmCommand.Execute(null);

        sut.DeleteCloudData = true;
        sut.DeleteConfirmation = "xoa";
        await sut.UnlinkCommand.ExecuteAsync(null);

        _links.UnlinkCalls.Should().Be(0);
        sut.StatusIsError.Should().BeTrue();
        sut.ShowUnlinkConfirm.Should().BeTrue();

        sut.DeleteConfirmation = "  XÓA ";
        await sut.UnlinkCommand.ExecuteAsync(null);

        _links.UnlinkCalls.Should().Be(1);
        _links.LastDeleteCloudData.Should().BeTrue();
        sut.IsCloudEnabled.Should().BeFalse();
        sut.ShowEnablePanel.Should().BeTrue();
        sut.ShowUnlinkConfirm.Should().BeFalse();
        sut.DeleteConfirmation.Should().BeEmpty();
        sut.StatusIsError.Should().BeFalse();
    }

    [Fact]
    public async Task Unlink_without_deletion_needs_no_confirmation()
    {
        _links.Snapshot = CloudTestData.Linked(_time.UtcNow.UtcDateTime);
        var sut = CreateSut();
        await sut.RefreshAsync();
        sut.OpenUnlinkConfirmCommand.Execute(null);

        await sut.UnlinkCommand.ExecuteAsync(null);

        _links.UnlinkCalls.Should().Be(1);
        _links.LastDeleteCloudData.Should().BeFalse();
        sut.IsCloudEnabled.Should().BeFalse();
    }

    [Fact]
    public async Task Unlink_remote_failure_reports_the_message_and_still_clears_local_state()
    {
        _links.Snapshot = CloudTestData.Linked(_time.UtcNow.UtcDateTime);
        _links.ThrowOnUnlink = new CloudSyncException(
            CloudErrorCodes.Network,
            "Đã ngắt liên kết trên máy này nhưng chưa thu hồi được trên máy chủ cloud. Hãy thử lại khi có mạng.",
            isNetworkError: true);

        var sut = CreateSut();
        await sut.RefreshAsync();
        sut.OpenUnlinkConfirmCommand.Execute(null);

        await sut.UnlinkCommand.ExecuteAsync(null);

        sut.IsCloudEnabled.Should().BeFalse();
        sut.StatusIsError.Should().BeTrue();
        sut.StatusMessage.Should().Contain("chưa thu hồi");
        _logger.Warnings.Should().ContainMatch("Cloud:*network*");
    }

    [Fact]
    public async Task Copy_pairing_code_uses_the_clipboard()
    {
        var sut = await CreatePairingSutAsync();

        sut.CopyPairingCodeCommand.Execute(null);

        _clipboard.LastText.Should().Be("ABCD2345");
        sut.StatusIsError.Should().BeFalse();
        sut.StatusMessage.Should().Contain("sao chép");
    }

    [Fact]
    public async Task Copy_failure_is_reported_in_vietnamese()
    {
        _clipboard.Result = false;
        var sut = await CreatePairingSutAsync();

        sut.CopyPairingCodeCommand.Execute(null);

        sut.StatusIsError.Should().BeTrue();
        sut.StatusMessage.Should().Contain("Không thể sao chép");
    }

    [Fact]
    public void Sync_now_wakes_the_outbox_worker()
    {
        var sut = CreateSut();

        sut.SyncNowCommand.Execute(null);

        _notifier.NotifyCalls.Should().Be(1);
        sut.StatusMessage.Should().Contain("đồng bộ ngay");
    }

    [Fact]
    public async Task Refresh_applies_linked_status_and_last_error()
    {
        _links.Snapshot = CloudTestData.Linked(
            new DateTime(2026, 11, 17, 2, 30, 0, DateTimeKind.Utc),
            lastError: "Máy chủ cloud tạm thời không phản hồi.");

        var sut = CreateSut();
        await sut.RefreshAsync();

        sut.IsLinked.Should().BeTrue();
        sut.ConsentAccepted.Should().BeTrue();
        sut.HasLastError.Should().BeTrue();
        sut.LastErrorText.Should().Contain("tạm thời");
        sut.LastSyncText.Should().NotBe("Chưa đồng bộ lần nào.");
        sut.ConnectionStateText.Should().Be("Đã ghép nối");
    }

    [Fact]
    public async Task Closing_the_window_drops_the_pairing_material()
    {
        var sut = await CreatePairingSutAsync();
        sut.HasPairingMaterial.Should().BeTrue();

        sut.HandleWindowClosed();

        sut.PairingCode.Should().BeEmpty();
        sut.QrImage.Should().BeNull();
        sut.HasPairingMaterial.Should().BeFalse();
        sut.StatusMessage.Should().BeEmpty();
    }

    private CloudLinkViewModel CreateSut() =>
        new(_links, _notifier, _clipboard, _logger, _time);

    private async Task<CloudLinkViewModel> CreatePairingSutAsync()
    {
        var sut = CreateSut();
        sut.ConsentAccepted = true;
        sut.CloudBaseUrl = CloudTestData.BaseUrl;
        await sut.EnableCommand.ExecuteAsync(null);
        return sut;
    }
}
