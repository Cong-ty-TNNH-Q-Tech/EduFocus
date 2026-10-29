using EduFocus.Ports.DTOs;

namespace EduFocus.Ports.Inbound;

/// <summary>
/// Supervises one camera session: selects the stored or default device, opens
/// and closes it, reports unavailable states (permission, busy, disconnected)
/// and retries with backoff. The attention pipeline captures frames through
/// this use case; callers must dispose each frame immediately after processing.
/// </summary>
public interface ICameraSessionUseCase
{
    CameraSessionState State { get; }

    string? ActiveDeviceId { get; }

    CameraFailureKind LastFailure { get; }

    IReadOnlyList<CameraDeviceInfo> Devices { get; }

    event EventHandler? StateChanged;

    Task<CameraOpenResult> StartAsync(CancellationToken cancellationToken = default);

    Task StopAsync(CancellationToken cancellationToken = default);

    Task<CameraCaptureResult> CaptureAsync(CancellationToken cancellationToken = default);

    Task<IReadOnlyList<CameraDeviceInfo>> RefreshDevicesAsync(CancellationToken cancellationToken = default);
}
