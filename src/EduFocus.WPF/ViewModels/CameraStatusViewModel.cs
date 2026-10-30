using System.Collections.ObjectModel;
using System.Windows.Media;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using EduFocus.Domain.Attention;
using EduFocus.Ports.DTOs;
using EduFocus.Ports.Inbound;
using EduFocus.Ports.Outbound.Persistence;
using EduFocus.Ports.Outbound.Services;
using EduFocus.UseCases.Attention;

namespace EduFocus.WPF.ViewModels;

/// <summary>
/// Camera indicator and preflight (UC08, issue #34). Projects the supervisor
/// state, explains unavailable states with the same guidance as the camera
/// check, and lets the parent run a short in-memory check and pick the device.
/// </summary>
public sealed partial class CameraStatusViewModel : ObservableObject, IDisposable
{
    private readonly ICameraSessionUseCase _camera;
    private readonly Func<ICameraCheckUseCase> _checkFactory;
    private readonly ICameraPlatformInfo _platform;
    private readonly IAppSettingsRepository _settings;
    private readonly IAppLogger _logger;
    private readonly TimeProvider _timeProvider;

    [ObservableProperty] private CameraSessionState _state = CameraSessionState.Off;
    [ObservableProperty] private string _statusText = "Camera đang tắt.";
    [ObservableProperty] private string _trayStatusText = "Camera: Đang tắt";
    [ObservableProperty] private Brush _indicatorBrush = Brushes.Gray;
    [ObservableProperty] private string _guidanceText = string.Empty;
    [ObservableProperty] private CameraDeviceInfo? _selectedDevice;
    [ObservableProperty] private bool _isBusy;
    [ObservableProperty] private string _feedbackText = string.Empty;
    [ObservableProperty] private bool _feedbackIsWarning;

    public CameraStatusViewModel(
        ICameraSessionUseCase camera,
        Func<ICameraCheckUseCase> checkFactory,
        ICameraPlatformInfo platform,
        IAppSettingsRepository settings,
        IAppLogger logger,
        TimeProvider timeProvider)
    {
        _camera = camera ?? throw new ArgumentNullException(nameof(camera));
        _checkFactory = checkFactory ?? throw new ArgumentNullException(nameof(checkFactory));
        _platform = platform ?? throw new ArgumentNullException(nameof(platform));
        _settings = settings ?? throw new ArgumentNullException(nameof(settings));
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
        _timeProvider = timeProvider ?? throw new ArgumentNullException(nameof(timeProvider));

        _camera.StateChanged += OnCameraStateChanged;
        Refresh();
    }

    public ObservableCollection<CameraDeviceInfo> Devices { get; } = [];

    public bool HasGuidance => GuidanceText.Length > 0;

    public bool HasFeedback => FeedbackText.Length > 0;

    public bool CanRunCheck => !IsBusy;

    public bool CanSaveDevice => !IsBusy && SelectedDevice is not null;

    partial void OnGuidanceTextChanged(string value) => OnPropertyChanged(nameof(HasGuidance));

    partial void OnFeedbackTextChanged(string value) => OnPropertyChanged(nameof(HasFeedback));

    partial void OnIsBusyChanged(bool value)
    {
        OnPropertyChanged(nameof(CanRunCheck));
        OnPropertyChanged(nameof(CanSaveDevice));
    }

    partial void OnSelectedDeviceChanged(CameraDeviceInfo? value) =>
        OnPropertyChanged(nameof(CanSaveDevice));

    public void Refresh()
    {
        State = _camera.State;
        StatusText = BuildStatusText();
        TrayStatusText = BuildTrayText();
        GuidanceText = BuildGuidanceText();
        IndicatorBrush = State switch
        {
            CameraSessionState.Active => Brushes.SeaGreen,
            CameraSessionState.Starting => Brushes.Goldenrod,
            CameraSessionState.Unavailable => Brushes.IndianRed,
            _ => Brushes.Gray
        };
    }

    public void Dispose() => _camera.StateChanged -= OnCameraStateChanged;

    [RelayCommand]
    private async Task RunCheckAsync()
    {
        if (IsBusy)
            return;

        IsBusy = true;
        SetFeedback("Đang kiểm tra camera trong vài giây...", false);

        try
        {
            var result = await _checkFactory().RunAsync();
            var guidance = CameraGuidance.Describe(
                result,
                _platform.SupportsMultiAppCamera,
                _camera.StoredDeviceSubstituted);

            SetFeedback(
                guidance is null
                    ? CameraGuidance.Summary(result)
                    : $"{CameraGuidance.Summary(result)} {guidance}",
                !result.IsReady);
        }
        catch (Exception ex)
        {
            _logger.Error(LogCategories.Attention, "The camera check failed unexpectedly.", ex);
            SetFeedback("Không thể kiểm tra camera lúc này. Vui lòng thử lại.", true);
        }
        finally
        {
            IsBusy = false;
            Refresh();
        }
    }

    [RelayCommand]
    public async Task LoadDevicesAsync()
    {
        if (IsBusy)
            return;

        IsBusy = true;

        try
        {
            var devices = await _camera.RefreshDevicesAsync();
            var storedDeviceId = await ReadStoredDeviceIdAsync();

            Devices.Clear();
            foreach (var device in devices)
                Devices.Add(device);

            SelectedDevice = storedDeviceId is null
                ? null
                : Devices.FirstOrDefault(
                    device => string.Equals(device.Id, storedDeviceId, StringComparison.OrdinalIgnoreCase));

            if (Devices.Count == 0)
            {
                SetFeedback(
                    "Không tìm thấy camera nào. Hãy kết nối webcam hoặc bật camera tích hợp rồi tải lại.",
                    true);
            }
            else if (storedDeviceId is not null && SelectedDevice is null)
            {
                SetFeedback(
                    "Camera đã lưu không còn kết nối. Hãy chọn lại camera bên dưới.",
                    true);
            }
            else
            {
                SetFeedback("Chọn camera EduFocus sẽ dùng khi giám sát.", false);
            }
        }
        catch (Exception ex)
        {
            _logger.Error(LogCategories.Attention, "Could not load the camera device list.", ex);
            SetFeedback("Không thể đọc danh sách camera. Vui lòng thử lại.", true);
        }
        finally
        {
            IsBusy = false;
        }
    }

    [RelayCommand]
    private async Task SaveDeviceAsync()
    {
        if (IsBusy || SelectedDevice is not { } device)
            return;

        IsBusy = true;

        try
        {
            await SaveDeviceIdAsync(device.Id);
            SetFeedback(
                $"Đã lưu camera “{device.DisplayName}”. Áp dụng từ lần giám sát tiếp theo.",
                false);
        }
        catch (Exception ex)
        {
            _logger.Error(LogCategories.Attention, "Could not save the selected camera device.", ex);
            SetFeedback("Không thể lưu camera đã chọn. Vui lòng thử lại.", true);
        }
        finally
        {
            IsBusy = false;
        }
    }

    [RelayCommand]
    private async Task UseDefaultDeviceAsync()
    {
        if (IsBusy)
            return;

        IsBusy = true;

        try
        {
            await SaveDeviceIdAsync(null);
            SelectedDevice = null;
            SetFeedback(
                "EduFocus sẽ dùng camera mặc định của Windows từ lần giám sát tiếp theo.",
                false);
        }
        catch (Exception ex)
        {
            _logger.Error(LogCategories.Attention, "Could not clear the stored camera device.", ex);
            SetFeedback("Không thể chuyển sang camera mặc định. Vui lòng thử lại.", true);
        }
        finally
        {
            IsBusy = false;
        }
    }

    private async Task SaveDeviceIdAsync(string? deviceId)
    {
        var settings = await _settings.GetAsync()
            ?? throw new InvalidOperationException("Application settings are not available yet.");

        settings.SetCameraDevice(deviceId, _timeProvider.GetUtcNow().UtcDateTime);
        await _settings.UpdateAsync(settings);
    }

    private async Task<string?> ReadStoredDeviceIdAsync()
    {
        try
        {
            var settings = await _settings.GetAsync();
            return settings?.CameraDeviceId;
        }
        catch (Exception ex)
        {
            _logger.Error(LogCategories.Attention, "Could not read the stored camera device.", ex);
            return null;
        }
    }

    private void SetFeedback(string message, bool isWarning)
    {
        FeedbackText = message;
        FeedbackIsWarning = isWarning;
    }

    private void OnCameraStateChanged(object? sender, EventArgs e)
    {
        var dispatcher = System.Windows.Application.Current?.Dispatcher;

        if (dispatcher is null || dispatcher.CheckAccess())
            Refresh();
        else
            dispatcher.InvokeAsync(Refresh);
    }

    private string BuildGuidanceText()
    {
        if (State == CameraSessionState.Unavailable)
        {
            return CameraGuidance.Describe(
                CameraProblemMapper.FromFailure(_camera.LastFailure),
                _platform.SupportsMultiAppCamera,
                _camera.StoredDeviceSubstituted) ?? string.Empty;
        }

        if (State == CameraSessionState.Active && _camera.StoredDeviceSubstituted)
            return CameraGuidance.SubstitutedDeviceNote;

        return string.Empty;
    }

    private string BuildTrayText() => State switch
    {
        CameraSessionState.Active => "Camera: Đang hoạt động",
        CameraSessionState.Starting => "Camera: Đang mở",
        CameraSessionState.Unavailable => "Camera: Không khả dụng",
        _ => "Camera: Đang tắt"
    };

    private string BuildStatusText() => State switch
    {
        CameraSessionState.Active =>
            "Camera đang hoạt động — khung hình chỉ được xử lý trong bộ nhớ và không được lưu lại.",
        CameraSessionState.Starting =>
            "Đang mở camera...",
        CameraSessionState.Unavailable => _camera.LastFailure switch
        {
            CameraFailureKind.PermissionDenied =>
                "Windows đã chặn quyền truy cập camera. Hãy bật quyền Camera cho EduFocus trong Cài đặt Windows.",
            CameraFailureKind.NotFound =>
                "Không tìm thấy camera. Hãy kết nối camera và kiểm tra lại.",
            CameraFailureKind.Busy =>
                "Camera đang được ứng dụng khác sử dụng. EduFocus sẽ thử lại sau ít phút.",
            CameraFailureKind.Disconnected =>
                "Camera đã bị ngắt kết nối. EduFocus sẽ thử lại sau ít phút.",
            CameraFailureKind.Timeout =>
                "Camera không phản hồi. EduFocus sẽ thử lại sau ít phút.",
            CameraFailureKind.NotStarted =>
                "Camera chưa được khởi động.",
            _ =>
                "Không thể mở camera. EduFocus sẽ thử lại sau ít phút."
        },
        _ =>
            "Camera đang tắt."
    };
}
