using EduFocus.Parent.App.Services;
using EduFocus.Parent.App.ViewModels;
using EduFocus.Parent.Core.Presentation;

namespace EduFocus.Parent.App.Views;

public partial class SettingsPage : ContentPage
{
    private readonly SettingsViewModel _viewModel;

    public SettingsPage()
    {
        InitializeComponent();
        _viewModel = ServiceHelper.GetService<SettingsViewModel>();
        BindingContext = _viewModel;
    }

    protected override void OnAppearing()
    {
        base.OnAppearing();
        _viewModel.Refresh();
        _ = RefreshPushAsync();
        _ = _viewModel.LoadDevicesAsync();
    }

    private async Task RefreshPushAsync()
    {
        await ServiceHelper.GetService<PushCoordinator>().SyncEndpointAsync();
        _viewModel.Refresh();
    }

    private async void OnPairClicked(object sender, EventArgs e) =>
        await Shell.Current.GoToAsync("//pairing");

    private async void OnEnableNotificationsClicked(object sender, EventArgs e)
    {
        try
        {
            if (await _viewModel.EnableNotificationsAsync(this))
                return;

            var open = await DisplayAlertAsync(
                "Thông báo đang tắt",
                "Mở Cài đặt hệ thống để bật quyền thông báo cho ứng dụng?",
                "Mở Cài đặt",
                "Để sau");
            if (open)
                AppInfo.Current.ShowSettingsUI();
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"Notification permission failed: {ex.GetType().Name}");
        }
    }

    private async void OnPrivacyTapped(object sender, TappedEventArgs e) =>
        await Shell.Current.GoToAsync("privacy");

    private async void OnRevokeDeviceClicked(object sender, EventArgs e)
    {
        if (sender is not Button { BindingContext: SettingsDeviceItem device })
            return;

        var confirmed = await DisplayAlertAsync(
            "Thu hồi thiết bị?",
            $"{device.Name} sẽ ngừng nhận thông báo và không xem được dữ liệu cho đến khi ghép nối lại.",
            "Thu hồi",
            "Hủy");
        if (!confirmed)
            return;

        if (!await _viewModel.RevokeDeviceAsync(device.Id))
        {
            await DisplayAlertAsync(
                "Không thu hồi được",
                "Vui lòng kiểm tra kết nối mạng và thử lại.",
                "OK");
        }
    }

    private async void OnDeleteCloudDataClicked(object sender, EventArgs e)
    {
        var confirmation = await DisplayPromptAsync(
            "Xóa dữ liệu trên cloud?",
            "Toàn bộ dữ liệu học tập và liên kết của gia đình trên cloud sẽ bị xóa vĩnh viễn. Nhập XÓA để xác nhận.",
            "Xóa vĩnh viễn",
            "Hủy",
            placeholder: "XÓA",
            maxLength: 8);
        if (confirmation is null)
            return;

        if (confirmation.Trim() != FamilyAdminService.DeleteConfirmation)
        {
            await DisplayAlertAsync(
                "Chưa xóa dữ liệu",
                "Bạn phải nhập đúng chữ XÓA để xác nhận.",
                "OK");
            return;
        }

        if (await _viewModel.DeleteCloudDataAsync())
        {
            await DisplayAlertAsync(
                "Đã xóa dữ liệu cloud",
                "Ứng dụng đã ngắt kết nối. Bạn có thể ghép nối lại bằng mã mới trên máy tính.",
                "OK");
            await Shell.Current.GoToAsync("//welcome");
        }
        else
        {
            await DisplayAlertAsync(
                "Không xóa được dữ liệu",
                "Kiểm tra kết nối mạng và thử lại.",
                "OK");
        }
    }

    private async void OnUnlinkClicked(object sender, EventArgs e)
    {
        var confirmed = await DisplayAlertAsync(
            "Ngắt kết nối?",
            "Điện thoại sẽ ngừng nhận thông báo và xóa mã truy cập. Dữ liệu học tập trên máy tính không bị ảnh hưởng.",
            "Ngắt kết nối",
            "Hủy");
        if (!confirmed)
            return;

        if (await _viewModel.UnlinkAsync())
        {
            await DisplayAlertAsync(
                "Đã ngắt kết nối",
                "Bạn có thể ghép nối lại bất cứ lúc nào bằng mã mới trên máy tính.",
                "OK");
            await Shell.Current.GoToAsync("//welcome");
        }
        else
        {
            await DisplayAlertAsync(
                "Không thể ngắt kết nối",
                "Vui lòng thử lại sau.",
                "OK");
        }
    }
}
