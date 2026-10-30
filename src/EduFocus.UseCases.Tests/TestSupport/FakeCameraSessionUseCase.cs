using EduFocus.Ports.DTOs;
using EduFocus.Ports.Inbound;

namespace EduFocus.UseCases.Tests.TestSupport;

internal sealed class FakeCameraSessionUseCase : ICameraSessionUseCase
{
    public CameraSessionState State { get; private set; } = CameraSessionState.Off;

    public string? ActiveDeviceId { get; set; }

    public CameraFailureKind LastFailure { get; set; } = CameraFailureKind.NotStarted;

    public bool StoredDeviceSubstituted { get; set; }

    public IReadOnlyList<CameraDeviceInfo> Devices { get; set; } = [];

    public event EventHandler? StateChanged;

    public CameraOpenResult StartResult { get; set; } = CameraOpenResult.Opened(null);

    public Func<CancellationToken, Task<CameraOpenResult>>? StartHandler { get; set; }

    public Queue<CameraCaptureResult> CaptureResults { get; } = new();

    public Func<CameraCaptureResult>? CaptureHandler { get; set; }

    public int StartCalls { get; private set; }

    public int StopCalls { get; private set; }

    public int CaptureCalls { get; private set; }

    public async Task<CameraOpenResult> StartAsync(CancellationToken cancellationToken = default)
    {
        StartCalls++;

        var result = StartHandler is null
            ? StartResult
            : await StartHandler(cancellationToken);

        if (result.Success)
        {
            State = CameraSessionState.Active;
            LastFailure = CameraFailureKind.None;
        }
        else
        {
            State = CameraSessionState.Unavailable;
            LastFailure = result.Failure;
        }

        StateChanged?.Invoke(this, EventArgs.Empty);
        return result;
    }

    public Task StopAsync(CancellationToken cancellationToken = default)
    {
        StopCalls++;
        State = CameraSessionState.Off;
        LastFailure = CameraFailureKind.NotStarted;
        StateChanged?.Invoke(this, EventArgs.Empty);
        return Task.CompletedTask;
    }

    public async Task<CameraCaptureResult> CaptureAsync(CancellationToken cancellationToken = default)
    {
        CaptureCalls++;

        // Yield so a blocking capture handler cannot run on the caller's stack;
        // callers can await the returned task while the handler is in flight.
        await Task.Yield();

        if (CaptureHandler is not null)
            return CaptureHandler();

        if (CaptureResults.TryDequeue(out var result))
            return result;

        return CameraCaptureResult.Unavailable(CameraFailureKind.NotStarted, "camera-not-started");
    }

    public Task<IReadOnlyList<CameraDeviceInfo>> RefreshDevicesAsync(
        CancellationToken cancellationToken = default) =>
        Task.FromResult(Devices);
}
