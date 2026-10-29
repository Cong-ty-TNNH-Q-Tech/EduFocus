using EduFocus.Ports.DTOs;
using EduFocus.Ports.Outbound.Services;

namespace EduFocus.UseCases.Tests.TestSupport;

internal sealed class FakeCameraPort : ICameraPort
{
    public bool IsOpen { get; private set; }

    public string? ActiveDeviceId { get; private set; }

    public IReadOnlyList<CameraDeviceInfo> Devices { get; set; } = [];

    public bool ThrowOnList { get; set; }

    public Action? OpenDelay { get; set; }

    public Func<CameraOpenResult>? OpenHandler { get; set; }

    public Queue<CameraOpenResult> OpenResults { get; } = new();

    public Func<CameraCaptureResult>? CaptureHandler { get; set; }

    public Queue<CameraCaptureResult> CaptureResults { get; } = new();

    public List<string?> OpenRequests { get; } = [];

    public int ListCalls { get; private set; }

    public int CaptureCalls { get; private set; }

    public int CloseCalls { get; private set; }

    public IReadOnlyList<CameraDeviceInfo> ListDevices()
    {
        ListCalls++;

        if (ThrowOnList)
            throw new InvalidOperationException("The camera catalog is unavailable.");

        return Devices;
    }

    public CameraOpenResult Open(string? deviceId)
    {
        OpenRequests.Add(deviceId);
        OpenDelay?.Invoke();

        var result = OpenHandler?.Invoke()
            ?? (OpenResults.Count > 0 ? OpenResults.Dequeue() : CameraOpenResult.Opened(deviceId));

        IsOpen = result.Success;
        ActiveDeviceId = result.Success ? result.DeviceId ?? deviceId : null;

        return result;
    }

    public CameraCaptureResult CaptureFrame()
    {
        CaptureCalls++;

        if (!IsOpen)
            return CameraCaptureResult.Unavailable(
                CameraFailureKind.Disconnected,
                "The camera is not open.");

        return CaptureHandler?.Invoke()
            ?? (CaptureResults.Count > 0
                ? CaptureResults.Dequeue()
                : CameraCaptureResult.Success(new CameraFrame(1, 1, [1, 2, 3])));
    }

    public void Close()
    {
        CloseCalls++;
        IsOpen = false;
        ActiveDeviceId = null;
    }

    public void Dispose() => Close();
}
