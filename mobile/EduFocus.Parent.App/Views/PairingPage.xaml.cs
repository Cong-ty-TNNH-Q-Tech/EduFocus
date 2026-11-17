using System.ComponentModel;
using EduFocus.Parent.App.Services;
using EduFocus.Parent.App.ViewModels;
using EduFocus.Parent.Core.Presentation;
using ZXing.Net.Maui;
using ZXing.Net.Maui.Controls;

namespace EduFocus.Parent.App.Views;

public partial class PairingPage : ContentPage, IQueryAttributable
{
    private readonly PairingViewModel _viewModel;
    private bool _handlingScan;

    public PairingPage()
    {
        InitializeComponent();
        _viewModel = ServiceHelper.GetService<PairingViewModel>();
        _viewModel.PropertyChanged += OnViewModelPropertyChanged;
        BindingContext = _viewModel;
    }

    public void ApplyQueryAttributes(IDictionary<string, object> query)
    {
        if (query.TryGetValue("data", out var value) && value is string data)
            _viewModel.PrefillFromLink(data);
    }

    protected override void OnAppearing()
    {
        base.OnAppearing();
        _viewModel.RefreshLinkedState();
        _viewModel.ConfirmReplaceAsync = ConfirmReplaceAsync;
        _viewModel.PairingSucceeded += OnPairingSucceeded;
    }

    protected override void OnDisappearing()
    {
        _viewModel.PairingSucceeded -= OnPairingSucceeded;
        _viewModel.ConfirmReplaceAsync = null;
        _viewModel.SetScannerActive(false);
        base.OnDisappearing();
    }

    private void OnViewModelPropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName == nameof(PairingViewModel.IsScannerActive))
            UpdateScanner();
    }

    private void UpdateScanner()
    {
        if (_viewModel.IsScannerActive)
        {
            if (ScannerHost.Content is not null)
                return;

            var scanner = new CameraBarcodeReaderView
            {
                IsDetecting = true,
                HorizontalOptions = LayoutOptions.Fill,
                VerticalOptions = LayoutOptions.Fill,
            };
            scanner.BarcodesDetected += OnBarcodesDetected;
            ScannerHost.Content = scanner;
        }
        else
        {
            if (ScannerHost.Content is CameraBarcodeReaderView scanner)
                scanner.BarcodesDetected -= OnBarcodesDetected;

            ScannerHost.Content = null;
        }
    }

    private async void OnBarcodesDetected(object? sender, BarcodeDetectionEventArgs e)
    {
        if (_handlingScan)
            return;

        var raw = e.Results?.FirstOrDefault()?.Value;
        if (string.IsNullOrWhiteSpace(raw))
            return;

        _handlingScan = true;
        try
        {
            _viewModel.SetScannerActive(false);
            await _viewModel.HandleScannedPayloadAsync(raw);
        }
        finally
        {
            _handlingScan = false;
        }
    }

    private async void OnToggleScannerClicked(object sender, EventArgs e)
    {
        if (_viewModel.IsScannerActive)
        {
            _viewModel.SetScannerActive(false);
            return;
        }

        var status = await Permissions.RequestAsync<Permissions.Camera>();
        if (status == PermissionStatus.Granted)
            _viewModel.SetScannerActive(true);
        else
            _viewModel.ReportCameraPermissionDenied();
    }

    private void OnOpenPermissionSettingsClicked(object sender, EventArgs e) =>
        AppInfo.Current.ShowSettingsUI();

    private async void OnBackClicked(object sender, EventArgs e)
    {
        _viewModel.SetScannerActive(false);
        await Shell.Current.GoToAsync(_viewModel.IsAlreadyLinked ? "//settings" : "//welcome");
    }

    private async Task<bool> ConfirmReplaceAsync() =>
        await DisplayAlertAsync(
            "Thay thế liên kết?",
            "Điện thoại này đang ghép nối với một máy tính. Ghép nối mã mới sẽ ngắt liên kết cũ.",
            "Thay thế",
            "Hủy");

    private async void OnPairingSucceeded(object? sender, EventArgs e)
    {
        ServiceHelper.GetService<SessionExpiryService>().Reset();

        await DisplayAlertAsync(
            "Ghép nối thành công",
            "Từ giờ bạn sẽ nhận thông báo và báo cáo tổng hợp từ máy tính.",
            "Xem tổng quan");

        try
        {
            var coordinator = ServiceHelper.GetService<PushCoordinator>();
            await coordinator.RequestPermissionAsync(this);
            _ = coordinator.SyncTokenAsync(force: true);
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"Push setup skipped: {ex.GetType().Name}");
        }

        await Shell.Current.GoToAsync("//overview");
    }
}
