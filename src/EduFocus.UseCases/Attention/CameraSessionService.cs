using EduFocus.Ports.DTOs;
using EduFocus.Ports.Inbound;
using EduFocus.Ports.Outbound.Persistence;
using EduFocus.Ports.Outbound.Services;

namespace EduFocus.UseCases.Attention;

/// <summary>
/// Camera supervisor (UC08, issue #30). Opens the stored or default device,
/// reports permission/busy/disconnected states, and reopens with exponential
/// backoff (30 seconds doubling to five minutes). Every blocking capture call
/// runs behind a timeout so a wedged driver cannot freeze the caller.
/// </summary>
public sealed class CameraSessionService : ICameraSessionUseCase
{
    private const string NotStartedMessage = "The camera session is not running.";
    private const string RetryScheduledMessage = "The camera is unavailable; a retry is scheduled.";
    private const string RetrySuppressedMessage = "The camera is unavailable; no retry is scheduled for this session.";
    private const string OpenTimeoutMessage = "Opening the camera timed out.";
    private const string CaptureTimeoutMessage = "Capturing a camera frame timed out.";
    private const string CaptureEmptyMessage = "The camera returned no frame.";

    private readonly ICameraPort _camera;
    private readonly IAppSettingsRepository _settings;
    private readonly IAppLogger _logger;
    private readonly TimeProvider _timeProvider;
    private readonly CameraOptions _options;
    private readonly SemaphoreSlim _gate = new(1, 1);

    private CameraSessionState _state = CameraSessionState.Off;
    private CameraFailureKind _lastFailure = CameraFailureKind.NotStarted;
    private string? _activeDeviceId;
    private IReadOnlyList<CameraDeviceInfo> _devices = [];
    private bool _storedDeviceSubstituted;
    private bool _retrySuppressed;
    private int _consecutiveCaptureFailures;
    private TimeSpan _retryDelay;
    private DateTime? _retryAtUtc;
    private int _stateVersion;

    public CameraSessionService(
        ICameraPort camera,
        IAppSettingsRepository settings,
        IAppLogger logger,
        TimeProvider timeProvider,
        CameraOptions options)
    {
        _camera = camera ?? throw new ArgumentNullException(nameof(camera));
        _settings = settings ?? throw new ArgumentNullException(nameof(settings));
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
        _timeProvider = timeProvider ?? throw new ArgumentNullException(nameof(timeProvider));

        if (options is null)
            throw new ArgumentNullException(nameof(options));
        if (options.OperationTimeout <= TimeSpan.Zero || options.OperationTimeout > TimeSpan.FromMinutes(1))
            throw new ArgumentOutOfRangeException(nameof(options), "Operation timeout must be between 0 and 60 seconds.");
        if (options.InitialRetryDelay <= TimeSpan.Zero)
            throw new ArgumentOutOfRangeException(nameof(options), "The initial retry delay must be positive.");
        if (options.MaxRetryDelay < options.InitialRetryDelay)
            throw new ArgumentOutOfRangeException(nameof(options), "The maximum retry delay cannot be shorter than the initial delay.");
        if (options.MaxConsecutiveCaptureFailures is < 1 or > 100)
            throw new ArgumentOutOfRangeException(nameof(options), "The capture failure threshold must be between 1 and 100.");

        _options = options;
    }

    public CameraSessionState State => _state;

    public string? ActiveDeviceId => _activeDeviceId;

    public CameraFailureKind LastFailure => _lastFailure;

    public bool StoredDeviceSubstituted => _storedDeviceSubstituted;

    public IReadOnlyList<CameraDeviceInfo> Devices => _devices;

    public event EventHandler? StateChanged;

    public async Task<CameraOpenResult> StartAsync(CancellationToken cancellationToken = default)
    {
        var version = _stateVersion;
        CameraOpenResult result;

        await _gate.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            if (_state == CameraSessionState.Active && _camera.IsOpen)
                return CameraOpenResult.Opened(_activeDeviceId);

            SetState(CameraSessionState.Starting);

            try
            {
                result = await OpenCoreAsync(cancellationToken).ConfigureAwait(false);
            }
            catch (OperationCanceledException)
            {
                ResetToOff();
                throw;
            }
        }
        finally
        {
            _gate.Release();
        }

        RaiseStateChangedIf(version);
        return result;
    }

    public async Task StopAsync(CancellationToken cancellationToken = default)
    {
        var version = _stateVersion;

        await _gate.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            CloseCamera();
            ResetToOff();
        }
        finally
        {
            _gate.Release();
        }

        RaiseStateChangedIf(version);
    }

    public async Task<CameraCaptureResult> CaptureAsync(CancellationToken cancellationToken = default)
    {
        var version = _stateVersion;
        CameraCaptureResult result;

        await _gate.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            result = await CaptureCoreAsync(cancellationToken).ConfigureAwait(false);
        }
        finally
        {
            _gate.Release();
        }

        RaiseStateChangedIf(version);
        return result;
    }

    public async Task<IReadOnlyList<CameraDeviceInfo>> RefreshDevicesAsync(
        CancellationToken cancellationToken = default)
    {
        await _gate.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            return await RefreshDevicesCoreAsync(cancellationToken).ConfigureAwait(false);
        }
        finally
        {
            _gate.Release();
        }
    }

    private async Task<CameraCaptureResult> CaptureCoreAsync(CancellationToken cancellationToken)
    {
        if (_state == CameraSessionState.Off)
            return CameraCaptureResult.Unavailable(CameraFailureKind.NotStarted, NotStartedMessage);

        if (_state != CameraSessionState.Active)
        {
            if (_retrySuppressed)
                return CameraCaptureResult.Unavailable(_lastFailure, RetrySuppressedMessage);

            if (_retryAtUtc is { } retryAt && UtcNow() < retryAt)
                return CameraCaptureResult.Unavailable(_lastFailure, RetryScheduledMessage);

            var open = await OpenCoreAsync(cancellationToken).ConfigureAwait(false);

            if (!open.Success)
                return CameraCaptureResult.Unavailable(open.Failure, open.Error);
        }

        CameraCaptureResult result;
        try
        {
            result = await RunBlockingAsync(_camera.CaptureFrame, cancellationToken).ConfigureAwait(false);
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (TimeoutException)
        {
            result = CameraCaptureResult.Unavailable(CameraFailureKind.Timeout, CaptureTimeoutMessage);
        }
        catch (Exception ex)
        {
            _logger.Warning(LogCategories.Attention, $"Camera capture threw an unexpected error: {ex.Message}");
            result = CameraCaptureResult.Unavailable(CameraFailureKind.Unknown, ex.Message);
        }

        if (result.Captured && result.Frame is not null)
        {
            _consecutiveCaptureFailures = 0;
            return result;
        }

        if (result.Captured)
            result = CameraCaptureResult.Unavailable(CameraFailureKind.Unknown, CaptureEmptyMessage);

        _consecutiveCaptureFailures++;

        var failure = result.Failure == CameraFailureKind.None
            ? CameraFailureKind.Unknown
            : result.Failure;

        if (failure == CameraFailureKind.Disconnected
            || _consecutiveCaptureFailures >= _options.MaxConsecutiveCaptureFailures)
        {
            CloseCamera();
            _activeDeviceId = null;
            _lastFailure = failure;
            SetState(CameraSessionState.Unavailable);
            ScheduleRetry();

            _logger.Warning(
                LogCategories.Attention,
                $"Camera session became unavailable after {_consecutiveCaptureFailures} capture failure(s): {failure}.");
        }

        return result;
    }

    private async Task<CameraOpenResult> OpenCoreAsync(CancellationToken cancellationToken)
    {
        _consecutiveCaptureFailures = 0;
        _retrySuppressed = false;

        await RefreshDevicesCoreAsync(cancellationToken).ConfigureAwait(false);

        var storedDeviceId = await ReadStoredDeviceIdAsync(cancellationToken).ConfigureAwait(false);
        var deviceId = ResolveDeviceId(storedDeviceId);

        CameraOpenResult result;
        try
        {
            result = await RunBlockingAsync(() => _camera.Open(deviceId), cancellationToken)
                .ConfigureAwait(false);
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (TimeoutException)
        {
            result = CameraOpenResult.Failed(CameraFailureKind.Timeout, OpenTimeoutMessage);
        }
        catch (Exception ex)
        {
            _logger.Warning(LogCategories.Attention, $"Opening the camera threw an unexpected error: {ex.Message}");
            result = CameraOpenResult.Failed(CameraFailureKind.Unknown, ex.Message);
        }

        if (result.Success)
        {
            _activeDeviceId = result.DeviceId ?? deviceId;
            _lastFailure = CameraFailureKind.None;
            ResetBackoff();
            SetState(CameraSessionState.Active);

            _logger.Info(LogCategories.Attention, "Camera session started.");
            return result;
        }

        _activeDeviceId = null;
        _lastFailure = result.Failure;
        SetState(CameraSessionState.Unavailable);

        if (ShouldRetry(result.Failure))
            ScheduleRetry();
        else
            SuppressRetry();

        _logger.Warning(LogCategories.Attention, $"Camera could not be opened: {result.Failure}.");
        return result;
    }

    private async Task<IReadOnlyList<CameraDeviceInfo>> RefreshDevicesCoreAsync(
        CancellationToken cancellationToken)
    {
        try
        {
            var devices = await RunBlockingAsync(_camera.ListDevices, cancellationToken).ConfigureAwait(false);
            _devices = devices ?? [];
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (TimeoutException)
        {
            _logger.Warning(LogCategories.Attention, "Listing camera devices timed out.");
        }
        catch (Exception ex)
        {
            _logger.Error(LogCategories.Attention, "Could not list camera devices.", ex);
        }

        return _devices;
    }

    private async Task<string?> ReadStoredDeviceIdAsync(CancellationToken cancellationToken)
    {
        try
        {
            var settings = await _settings.GetAsync(cancellationToken).ConfigureAwait(false);
            return settings?.CameraDeviceId;
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (Exception ex)
        {
            _logger.Error(
                LogCategories.Attention,
                "Could not read the stored camera device; using the default camera.",
                ex);
            return null;
        }
    }

    private string? ResolveDeviceId(string? storedDeviceId)
    {
        _storedDeviceSubstituted = false;

        if (string.IsNullOrWhiteSpace(storedDeviceId))
            return null;

        if (_devices.Count == 0)
        {
            _logger.Warning(
                LogCategories.Attention,
                "Camera devices could not be listed; using the default camera.");
            return null;
        }

        if (_devices.Any(device => string.Equals(device.Id, storedDeviceId, StringComparison.OrdinalIgnoreCase)))
            return storedDeviceId;

        _storedDeviceSubstituted = true;
        _logger.Warning(
            LogCategories.Attention,
            "The stored camera device is no longer connected; using the default camera.");
        return null;
    }

    private async Task<T> RunBlockingAsync<T>(Func<T> action, CancellationToken cancellationToken)
    {
        var task = Task.Run(action, CancellationToken.None);

        try
        {
            return await task.WaitAsync(_options.OperationTimeout, cancellationToken).ConfigureAwait(false);
        }
        catch (TimeoutException)
        {
            // Observe the eventual outcome so a late failure is not unobserved.
            _ = task.ContinueWith(
                static completed => _ = completed.Exception,
                CancellationToken.None,
                TaskContinuationOptions.OnlyOnFaulted | TaskContinuationOptions.ExecuteSynchronously,
                TaskScheduler.Default);
            throw;
        }
    }

    private void CloseCamera()
    {
        try
        {
            _camera.Close();
        }
        catch (Exception ex)
        {
            _logger.Error(LogCategories.Attention, "Could not close the camera.", ex);
        }
    }

    private void ResetToOff()
    {
        _activeDeviceId = null;
        _lastFailure = CameraFailureKind.NotStarted;
        _consecutiveCaptureFailures = 0;
        _storedDeviceSubstituted = false;
        ResetBackoff();
        SetState(CameraSessionState.Off);
    }

    private void ScheduleRetry()
    {
        _retrySuppressed = false;
        var delay = _retryDelay > TimeSpan.Zero ? _retryDelay : _options.InitialRetryDelay;

        _retryAtUtc = UtcNow() + delay;
        _retryDelay = TimeSpan.FromTicks(Math.Min(delay.Ticks * 2, _options.MaxRetryDelay.Ticks));
    }

    private void SuppressRetry()
    {
        _retrySuppressed = true;
        _retryAtUtc = null;
        _retryDelay = TimeSpan.Zero;
    }

    // A missing device cannot appear without the OS re-enumerating; retrying
    // every few seconds would only wake the driver. An explicit StartAsync (for
    // example from the camera check) always attempts a fresh open.
    private static bool ShouldRetry(CameraFailureKind failure) =>
        failure != CameraFailureKind.NotFound;

    private void ResetBackoff()
    {
        _retryDelay = TimeSpan.Zero;
        _retryAtUtc = null;
    }

    private void SetState(CameraSessionState state)
    {
        if (_state == state)
            return;

        _state = state;
        _stateVersion++;
    }

    private void RaiseStateChangedIf(int previousVersion)
    {
        if (_stateVersion == previousVersion)
            return;

        try
        {
            StateChanged?.Invoke(this, EventArgs.Empty);
        }
        catch (Exception ex)
        {
            _logger.Error(LogCategories.Attention, "A camera state subscriber threw an exception.", ex);
        }
    }

    private DateTime UtcNow() => _timeProvider.GetUtcNow().UtcDateTime;
}
