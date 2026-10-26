using System.Diagnostics;
using OpenCvSharp;

namespace EduFocus.Infrastructure.Attention.Camera;

internal sealed record CameraOpenResult(bool Success, double ElapsedMs, string Api, string? Error);

internal sealed class OpenCvCameraSource : IDisposable
{
    private readonly int _cameraIndex;
    private readonly string _captureApi;
    private readonly int _width;
    private readonly int _height;
    private VideoCapture? _capture;

    public OpenCvCameraSource(int cameraIndex, string captureApi, int width, int height)
    {
        _cameraIndex = cameraIndex;
        _captureApi = captureApi;
        _width = width;
        _height = height;
    }

    public bool IsOpen => _capture?.IsOpened() == true;

    public CameraOpenResult Open()
    {
        Dispose();
        var sw = Stopwatch.StartNew();
        VideoCapture? capture = null;
        try
        {
            var api = _captureApi.Equals("dshow", StringComparison.OrdinalIgnoreCase)
                ? VideoCaptureAPIs.DSHOW
                : VideoCaptureAPIs.MSMF;

            capture = new VideoCapture();
            capture.Open(_cameraIndex, api);
            capture.Set(VideoCaptureProperties.FrameWidth, _width);
            capture.Set(VideoCaptureProperties.FrameHeight, _height);

            sw.Stop();
            if (!capture.IsOpened())
            {
                capture.Dispose();
                return new CameraOpenResult(false, sw.Elapsed.TotalMilliseconds, _captureApi, "camera-open-failed");
            }

            _capture = capture;
            capture = null;
            return new CameraOpenResult(true, sw.Elapsed.TotalMilliseconds, _captureApi, null);
        }
        catch (Exception ex)
        {
            sw.Stop();
            capture?.Dispose();
            return new CameraOpenResult(false, sw.Elapsed.TotalMilliseconds, _captureApi, $"{ex.GetType().Name}: {ex.Message}");
        }
    }

    public bool TryCapture(Mat destination, int flushFrames, out double captureMs, out string? error)
    {
        var sw = Stopwatch.StartNew();
        error = null;
        try
        {
            if (_capture == null || !_capture.IsOpened())
            {
                error = "camera-not-open";
                captureMs = sw.Elapsed.TotalMilliseconds;
                return false;
            }

            for (var i = 0; i < flushFrames; i++)
                _capture.Grab();

            if (!_capture.Read(destination) || destination.Empty())
            {
                error = "frame-read-failed";
                captureMs = sw.Elapsed.TotalMilliseconds;
                return false;
            }

            captureMs = sw.Elapsed.TotalMilliseconds;
            return true;
        }
        catch (Exception ex)
        {
            error = $"{ex.GetType().Name}: {ex.Message}";
            captureMs = sw.Elapsed.TotalMilliseconds;
            return false;
        }
    }

    public void Dispose()
    {
        _capture?.Release();
        _capture?.Dispose();
        _capture = null;
    }
}
