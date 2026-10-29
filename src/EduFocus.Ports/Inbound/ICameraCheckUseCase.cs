using EduFocus.Domain.Attention;

namespace EduFocus.Ports.Inbound;

/// <summary>
/// Short camera check/calibration run before monitoring starts (UC08, issue
/// #34). It opens the camera, samples a few frames and reports whether frames
/// arrive, are bright enough and contain a face. Frames are analyzed in memory
/// and disposed immediately; nothing is retained.
/// </summary>
public interface ICameraCheckUseCase
{
    Task<CameraHealthResult> RunAsync(CancellationToken cancellationToken = default);
}
