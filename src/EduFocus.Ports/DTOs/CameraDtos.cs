namespace EduFocus.Ports.DTOs;

/// <summary>
/// A camera as reported by the platform enumerator. <see cref="Index"/> is the
/// ordinal the capture backend uses; <see cref="Id"/> is the stable device
/// identity persisted in <c>AppSettings.CameraDeviceId</c>.
/// </summary>
public sealed record CameraDeviceInfo(string Id, string DisplayName, int Index);

/// <summary>Why a camera open or frame capture failed.</summary>
public enum CameraFailureKind
{
    None,
    NotStarted,
    NotFound,
    PermissionDenied,
    Busy,
    Disconnected,
    Timeout,
    Unknown
}

/// <summary>Camera lifecycle exposed to the UI and the attention pipeline.</summary>
public enum CameraSessionState
{
    Off,
    Starting,
    Active,
    Unavailable
}

/// <summary>
/// A single BGR24 frame copied out of the capture backend. Native captures are
/// released before the frame is returned, so disposing releases the managed
/// buffer early; nothing is written to disk or sent over the network.
/// </summary>
public sealed class CameraFrame : IDisposable
{
    private byte[]? _pixels;

    public CameraFrame(int width, int height, byte[] bgr24)
    {
        if (width <= 0)
            throw new ArgumentOutOfRangeException(nameof(width), width, "Width must be positive.");
        if (height <= 0)
            throw new ArgumentOutOfRangeException(nameof(height), height, "Height must be positive.");
        ArgumentNullException.ThrowIfNull(bgr24);

        var required = checked(width * height * 3);
        if (bgr24.Length < required)
            throw new ArgumentException("The frame buffer is smaller than width × height × 3.", nameof(bgr24));

        Width = width;
        Height = height;
        _pixels = bgr24;
    }

    public int Width { get; }

    public int Height { get; }

    public bool IsDisposed => _pixels is null;

    public ReadOnlySpan<byte> Pixels =>
        _pixels ?? throw new ObjectDisposedException(nameof(CameraFrame));

    public void Dispose() => _pixels = null;
}

public sealed record CameraOpenResult(
    bool Success,
    CameraFailureKind Failure,
    string? Error,
    string? DeviceId)
{
    public static CameraOpenResult Opened(string? deviceId) =>
        new(true, CameraFailureKind.None, null, deviceId);

    public static CameraOpenResult Failed(CameraFailureKind failure, string? error) =>
        new(false, failure, error, null);
}

public sealed record CameraCaptureResult(
    bool Captured,
    CameraFailureKind Failure,
    string? Error,
    CameraFrame? Frame)
{
    public static CameraCaptureResult Success(CameraFrame frame) =>
        new(true, CameraFailureKind.None, null, frame);

    public static CameraCaptureResult Unavailable(CameraFailureKind failure, string? error) =>
        new(false, failure, error, null);
}
