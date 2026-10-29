using System.Runtime.InteropServices;
using EduFocus.Ports.DTOs;
using EduFocus.Ports.Outbound.Services;
using OpenCvSharp;

namespace EduFocus.Infrastructure.Attention.Camera;

/// <summary>
/// OpenCV capture adapter behind <see cref="ICameraPort"/>. The native
/// <see cref="Mat"/> is converted to a managed BGR24 buffer and disposed before
/// the frame is returned, so no native state and no frame bytes leave the
/// process or touch disk.
/// </summary>
public sealed class OpenCvCameraPort : ICameraPort
{
    private readonly ICameraDeviceCatalog _catalog;
    private readonly CameraPortOptions _options;

    private OpenCvCameraSource? _source;
    private IReadOnlyList<CameraDeviceInfo> _devices = [];
    private string? _activeDeviceId;

    public OpenCvCameraPort(ICameraDeviceCatalog catalog, CameraPortOptions options)
    {
        _catalog = catalog ?? throw new ArgumentNullException(nameof(catalog));
        _options = options ?? throw new ArgumentNullException(nameof(options));
    }

    public bool IsOpen => _source?.IsOpen == true;

    public string? ActiveDeviceId => _activeDeviceId;

    public IReadOnlyList<CameraDeviceInfo> ListDevices()
    {
        _devices = _catalog.ListDevices();
        return _devices;
    }

    public CameraOpenResult Open(string? deviceId)
    {
        Close();
        _devices = _catalog.ListDevices();

        var index = 0;

        if (!string.IsNullOrWhiteSpace(deviceId))
        {
            var match = _devices.FirstOrDefault(
                device => string.Equals(device.Id, deviceId, StringComparison.OrdinalIgnoreCase));

            if (match is null)
                return CameraOpenResult.Failed(
                    CameraFailureKind.NotFound,
                    "The selected camera is not connected.");

            index = match.Index;
        }

        var source = new OpenCvCameraSource(index, _options.CaptureApi, _options.Width, _options.Height);
        var opened = source.Open();

        if (!opened.Success)
        {
            source.Dispose();
            return CameraOpenResult.Failed(ClassifyOpenFailure(opened.Error), opened.Error);
        }

        _source = source;
        _activeDeviceId = deviceId ?? _devices.FirstOrDefault(device => device.Index == index)?.Id;

        return CameraOpenResult.Opened(_activeDeviceId);
    }

    public CameraCaptureResult CaptureFrame()
    {
        var source = _source;

        if (source is null || !source.IsOpen)
            return CameraCaptureResult.Unavailable(
                CameraFailureKind.Disconnected,
                "The camera is not open.");

        try
        {
            using var mat = new Mat();

            if (!source.TryCapture(mat, _options.FlushFrames, out _, out var error) || mat.Empty())
            {
                var failure = string.Equals(error, "camera-not-open", StringComparison.Ordinal)
                    ? CameraFailureKind.Disconnected
                    : CameraFailureKind.Unknown;

                return CameraCaptureResult.Unavailable(failure, error ?? "frame-read-failed");
            }

            return CameraCaptureResult.Success(CopyToManagedFrame(mat));
        }
        catch (Exception ex)
        {
            return CameraCaptureResult.Unavailable(CameraFailureKind.Unknown, ex.Message);
        }
    }

    public void Close()
    {
        _source?.Dispose();
        _source = null;
        _activeDeviceId = null;
    }

    public void Dispose() => Close();

    private CameraFailureKind ClassifyOpenFailure(string? error)
    {
        if (!string.IsNullOrWhiteSpace(error))
        {
            if (Contains(error, "access") || Contains(error, "denied") || Contains(error, "permission"))
                return CameraFailureKind.PermissionDenied;
            if (Contains(error, "busy") || Contains(error, "in use") || Contains(error, "used by another"))
                return CameraFailureKind.Busy;
            if (Contains(error, "not found") || Contains(error, "no device"))
                return CameraFailureKind.NotFound;
        }

        // OpenCV usually reports a bare open failure. No enumerated device means
        // the camera is disconnected; otherwise the device is present but could
        // not be opened, which in practice means another application holds it.
        return _devices.Count == 0 ? CameraFailureKind.NotFound : CameraFailureKind.Busy;
    }

    private static bool Contains(string value, string fragment) =>
        value.Contains(fragment, StringComparison.OrdinalIgnoreCase);

    private static CameraFrame CopyToManagedFrame(Mat mat)
    {
        Mat? converted = null;

        try
        {
            var bgr = mat;

            if (mat.Type() != MatType.CV_8UC3)
            {
                converted = new Mat();

                if (mat.Channels() == 4)
                    Cv2.CvtColor(mat, converted, ColorConversionCodes.BGRA2BGR);
                else if (mat.Channels() == 1)
                    Cv2.CvtColor(mat, converted, ColorConversionCodes.GRAY2BGR);
                else
                    throw new InvalidOperationException($"Unsupported camera pixel format: {mat.Type()}.");

                bgr = converted;
            }

            var width = bgr.Cols;
            var height = bgr.Rows;
            var buffer = new byte[checked(width * height * 3)];

            if (bgr.IsContinuous())
            {
                Marshal.Copy(bgr.Data, buffer, 0, buffer.Length);
            }
            else
            {
                var rowBytes = width * 3;

                for (var row = 0; row < height; row++)
                    Marshal.Copy(bgr.Ptr(row), buffer, row * rowBytes, rowBytes);
            }

            return new CameraFrame(width, height, buffer);
        }
        finally
        {
            converted?.Dispose();
        }
    }
}
