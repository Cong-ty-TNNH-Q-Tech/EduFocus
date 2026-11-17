using EduFocus.Parent.App.Services;
using EduFocus.Parent.App.ViewModels;

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
        _ = ServiceHelper.GetService<PushCoordinator>().SyncTokenAsync();
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
        await Shell.Current.GoToAsync("//privacy");

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
