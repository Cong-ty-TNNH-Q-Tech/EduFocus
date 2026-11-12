using EduFocus.Domain.Attention;
using EduFocus.Ports.DTOs;

namespace EduFocus.Ports.Inbound;

/// <summary>
/// Short camera check/calibration run before monitoring starts (UC08, issue
/// #34). It opens the camera, samples a few frames and reports whether frames
/// arrive, are bright enough and contain a face. Frames are analyzed in memory
/// and disposed immediately; nothing is retained. When <paramref name="preview"/>
/// is supplied, a transient copy of each captured frame is reported for on-screen
/// display only — it is never persisted.
/// </summary>
public interface ICameraCheckUseCase
{
    Task<CameraHealthResult> RunAsync(
        CancellationToken cancellationToken = default,
        IProgress<CameraPreviewFrame>? preview = null);
}
