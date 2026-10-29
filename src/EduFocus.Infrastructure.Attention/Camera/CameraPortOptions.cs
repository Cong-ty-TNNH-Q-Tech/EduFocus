namespace EduFocus.Infrastructure.Attention.Camera;

/// <summary>
/// Capture backend settings (issue #30). MSMF is the default on Windows 10/11;
/// the first frame is flushed so analyzers do not score a stale buffer.
/// </summary>
public sealed class CameraPortOptions
{
    public string CaptureApi { get; init; } = "msmf";

    public int Width { get; init; } = 640;

    public int Height { get; init; } = 480;

    public int FlushFrames { get; init; } = 1;
}
