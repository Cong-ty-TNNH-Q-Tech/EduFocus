using EduFocus.Ports.DTOs;

namespace EduFocus.Ports.Outbound.Services;

/// <summary>
/// Camera device abstraction (issue #30). Implementations may use OpenCV,
/// DirectShow or a fake; frames leave the adapter as short-lived managed
/// buffers and native resources are released inside <see cref="CaptureFrame"/>.
/// </summary>
public interface ICameraPort : IDisposable
{
    bool IsOpen { get; }

    string? ActiveDeviceId { get; }

    IReadOnlyList<CameraDeviceInfo> ListDevices();

    CameraOpenResult Open(string? deviceId);

    CameraCaptureResult CaptureFrame();

    void Close();
}
