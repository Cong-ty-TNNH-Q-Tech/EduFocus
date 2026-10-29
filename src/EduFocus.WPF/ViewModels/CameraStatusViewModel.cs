using System.Windows.Media;
using CommunityToolkit.Mvvm.ComponentModel;
using EduFocus.Ports.DTOs;
using EduFocus.Ports.Inbound;

namespace EduFocus.WPF.ViewModels;

/// <summary>
/// Read-only camera indicator (UC08). The attention pipeline owns the camera
/// lifecycle; this view model only projects the supervisor state so the main
/// window and the tray explain why frames are or are not being analyzed.
/// </summary>
public sealed partial class CameraStatusViewModel : ObservableObject, IDisposable
{
    private readonly ICameraSessionUseCase _camera;

    [ObservableProperty] private CameraSessionState _state = CameraSessionState.Off;
    [ObservableProperty] private string _statusText = "Camera đang tắt.";
    [ObservableProperty] private string _trayStatusText = "Camera: Đang tắt";
    [ObservableProperty] private Brush _indicatorBrush = Brushes.Gray;

    public CameraStatusViewModel(ICameraSessionUseCase camera)
    {
        _camera = camera ?? throw new ArgumentNullException(nameof(camera));
        _camera.StateChanged += OnCameraStateChanged;
        Refresh();
    }

    public void Refresh()
    {
        State = _camera.State;
        StatusText = BuildStatusText();
        TrayStatusText = BuildTrayText();
        IndicatorBrush = State switch
        {
            CameraSessionState.Active => Brushes.SeaGreen,
            CameraSessionState.Starting => Brushes.Goldenrod,
            CameraSessionState.Unavailable => Brushes.IndianRed,
            _ => Brushes.Gray
        };
    }

    public void Dispose() => _camera.StateChanged -= OnCameraStateChanged;

    private void OnCameraStateChanged(object? sender, EventArgs e)
    {
        var dispatcher = System.Windows.Application.Current?.Dispatcher;

        if (dispatcher is null || dispatcher.CheckAccess())
            Refresh();
        else
            dispatcher.InvokeAsync(Refresh);
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
                "Không tìm thấy camera. Hãy kết nối camera và thử lại.",
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
