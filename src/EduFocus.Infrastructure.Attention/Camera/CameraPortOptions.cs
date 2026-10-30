namespace EduFocus.Infrastructure.Attention.Camera;

/// <summary>
/// Capture backend settings (issue #30). DirectShow is the default because
/// Media Foundation needs 6-8 seconds to open and negotiate a UVC webcam on
/// some machines, longer than the 5-second camera supervisor timeout; MSMF
/// remains selectable through <see cref="CaptureApi"/>. The first frame is
/// flushed so analyzers do not score a stale buffer.
/// </summary>
public sealed class CameraPortOptions
{
    public string CaptureApi { get; init; } = "dshow";

    public int Width { get; init; } = 640;

    public int Height { get; init; } = 480;

    public int FlushFrames { get; init; } = 1;
}
