using EduFocus.Domain.Entities;
using EduFocus.Ports.DTOs;
using EduFocus.UseCases.Attention;
using EduFocus.UseCases.Tests.TestSupport;
using FluentAssertions;

namespace EduFocus.UseCases.Tests.Attention;

public sealed class CameraSessionServiceTests
{
    private static readonly DateTime Now = new(2026, 10, 30, 9, 0, 0, DateTimeKind.Utc);

    [Fact]
    public async Task Start_opens_the_stored_device()
    {
        var harness = new Harness();
        harness.Settings.Stored = SettingsWithCamera("cam-b");
        harness.Camera.Devices = [Device("cam-a", 0), Device("cam-b", 1)];

        var result = await harness.Service.StartAsync();

        result.Success.Should().BeTrue();
        result.DeviceId.Should().Be("cam-b");
        harness.Camera.OpenRequests.Should().Equal("cam-b");
        harness.Service.State.Should().Be(CameraSessionState.Active);
        harness.Service.ActiveDeviceId.Should().Be("cam-b");
        harness.Service.LastFailure.Should().Be(CameraFailureKind.None);
    }

    [Fact]
    public async Task Start_falls_back_to_the_default_camera_when_the_stored_device_is_missing()
    {
        var harness = new Harness();
        harness.Settings.Stored = SettingsWithCamera("cam-removed");
        harness.Camera.Devices = [Device("cam-a", 0)];

        var result = await harness.Service.StartAsync();

        result.Success.Should().BeTrue();
        harness.Camera.OpenRequests.Should().Equal((string?)null);
        harness.Service.StoredDeviceSubstituted.Should().BeTrue();
        harness.Logger.Warnings.Should().Contain(
            message => message.Contains("stored camera device is no longer connected"));
    }

    [Fact]
    public async Task Start_uses_the_default_camera_when_no_device_is_stored()
    {
        var harness = new Harness();
        harness.Camera.Devices = [Device("cam-a", 0)];

        var result = await harness.Service.StartAsync();

        result.Success.Should().BeTrue();
        harness.Camera.OpenRequests.Should().Equal((string?)null);
        harness.Service.State.Should().Be(CameraSessionState.Active);
        harness.Service.StoredDeviceSubstituted.Should().BeFalse();
    }

    [Fact]
    public async Task Start_uses_the_default_camera_when_devices_cannot_be_listed()
    {
        var harness = new Harness();
        harness.Settings.Stored = SettingsWithCamera("cam-b");

        var result = await harness.Service.StartAsync();

        result.Success.Should().BeTrue();
        harness.Camera.OpenRequests.Should().Equal((string?)null);
        harness.Logger.Warnings.Should().Contain(
            message => message.Contains("Camera devices could not be listed"));
    }

    [Fact]
    public async Task Start_uses_the_default_camera_when_settings_cannot_be_read()
    {
        var harness = new Harness();
        harness.Settings.ThrowOnRead = true;
        harness.Camera.Devices = [Device("cam-a", 0)];

        var result = await harness.Service.StartAsync();

        result.Success.Should().BeTrue();
        harness.Camera.OpenRequests.Should().Equal((string?)null);
        harness.Logger.Errors.Should().Contain(
            message => message.Contains("Could not read the stored camera device"));
    }

    [Fact]
    public async Task Start_is_idempotent_while_the_camera_is_active()
    {
        var harness = new Harness();

        await harness.Service.StartAsync();
        var second = await harness.Service.StartAsync();

        second.Success.Should().BeTrue();
        harness.Camera.OpenRequests.Should().ContainSingle();
        harness.Camera.CloseCalls.Should().Be(0);
    }

    [Fact]
    public async Task Start_reports_permission_denied_and_waits_for_the_retry_delay()
    {
        var harness = new Harness();
        harness.Camera.OpenResults.Enqueue(
            CameraOpenResult.Failed(CameraFailureKind.PermissionDenied, "access denied"));

        var result = await harness.Service.StartAsync();

        result.Success.Should().BeFalse();
        result.Failure.Should().Be(CameraFailureKind.PermissionDenied);
        harness.Service.State.Should().Be(CameraSessionState.Unavailable);
        harness.Service.LastFailure.Should().Be(CameraFailureKind.PermissionDenied);

        var gated = await harness.Service.CaptureAsync();
        gated.Failure.Should().Be(CameraFailureKind.PermissionDenied);
        harness.Camera.OpenRequests.Should().ContainSingle();
        harness.Camera.CaptureCalls.Should().Be(0);

        harness.Clock.Advance(TimeSpan.FromSeconds(29));
        (await harness.Service.CaptureAsync()).Failure.Should().Be(CameraFailureKind.PermissionDenied);
        harness.Camera.OpenRequests.Should().ContainSingle();

        harness.Clock.Advance(TimeSpan.FromSeconds(1));
        harness.Camera.OpenResults.Enqueue(CameraOpenResult.Opened("cam-a"));

        var retried = await harness.Service.CaptureAsync();

        retried.Captured.Should().BeTrue();
        harness.Camera.OpenRequests.Should().HaveCount(2);
        harness.Service.State.Should().Be(CameraSessionState.Active);
    }

    [Fact]
    public async Task Retries_back_off_exponentially_up_to_the_cap()
    {
        var harness = new Harness();
        harness.Camera.Devices = [Device("cam-a", 0)];
        harness.Camera.OpenResults.Enqueue(
            CameraOpenResult.Failed(CameraFailureKind.Busy, "in use"));
        harness.Camera.OpenResults.Enqueue(
            CameraOpenResult.Failed(CameraFailureKind.Busy, "in use"));
        harness.Camera.OpenResults.Enqueue(CameraOpenResult.Opened("cam-a"));

        await harness.Service.StartAsync();

        (await harness.Service.CaptureAsync()).Failure.Should().Be(CameraFailureKind.Busy);
        harness.Camera.OpenRequests.Should().ContainSingle();

        harness.Clock.Advance(TimeSpan.FromSeconds(30));
        (await harness.Service.CaptureAsync()).Failure.Should().Be(CameraFailureKind.Busy);
        harness.Camera.OpenRequests.Should().HaveCount(2);

        harness.Clock.Advance(TimeSpan.FromSeconds(30));
        (await harness.Service.CaptureAsync()).Failure.Should().Be(CameraFailureKind.Busy);
        harness.Camera.OpenRequests.Should().HaveCount(2);

        harness.Clock.Advance(TimeSpan.FromSeconds(30));
        var retried = await harness.Service.CaptureAsync();

        retried.Captured.Should().BeTrue();
        harness.Camera.OpenRequests.Should().HaveCount(3);
        harness.Service.State.Should().Be(CameraSessionState.Active);
    }

    [Fact]
    public async Task A_missing_device_suppresses_retries_until_the_camera_is_started_again()
    {
        var harness = new Harness();
        harness.Camera.OpenResults.Enqueue(CameraOpenResult.Failed(CameraFailureKind.NotFound, "no device"));

        var opened = await harness.Service.StartAsync();

        opened.Success.Should().BeFalse();
        opened.Failure.Should().Be(CameraFailureKind.NotFound);
        harness.Service.State.Should().Be(CameraSessionState.Unavailable);

        var gated = await harness.Service.CaptureAsync();

        gated.Failure.Should().Be(CameraFailureKind.NotFound);
        gated.Error.Should().Contain("no retry");
        harness.Camera.OpenRequests.Should().ContainSingle();
        harness.Camera.CaptureCalls.Should().Be(0);

        harness.Clock.Advance(TimeSpan.FromMinutes(30));
        (await harness.Service.CaptureAsync()).Failure.Should().Be(CameraFailureKind.NotFound);
        harness.Camera.OpenRequests.Should().ContainSingle();

        harness.Camera.OpenResults.Enqueue(CameraOpenResult.Opened("cam-a"));
        var restarted = await harness.Service.StartAsync();

        restarted.Success.Should().BeTrue();
        harness.Camera.OpenRequests.Should().HaveCount(2);
        harness.Service.State.Should().Be(CameraSessionState.Active);
    }

    [Fact]
    public async Task Capture_before_start_returns_not_started()
    {
        var harness = new Harness();

        var result = await harness.Service.CaptureAsync();

        result.Captured.Should().BeFalse();
        result.Failure.Should().Be(CameraFailureKind.NotStarted);
        harness.Service.State.Should().Be(CameraSessionState.Off);
        harness.Camera.OpenRequests.Should().BeEmpty();
        harness.Camera.CaptureCalls.Should().Be(0);
    }

    [Fact]
    public async Task Capture_returns_an_undisposed_frame()
    {
        var harness = new Harness();
        await harness.Service.StartAsync();

        var result = await harness.Service.CaptureAsync();

        result.Captured.Should().BeTrue();
        result.Frame.Should().NotBeNull();
        result.Frame!.IsDisposed.Should().BeFalse();
        result.Frame.Pixels.Length.Should().Be(3);

        result.Frame.Dispose();
        result.Frame.IsDisposed.Should().BeTrue();
    }

    [Fact]
    public async Task A_disconnect_marks_the_camera_unavailable_and_reopens_after_the_delay()
    {
        var harness = new Harness();
        harness.Camera.OpenResults.Enqueue(CameraOpenResult.Opened("cam-a"));
        await harness.Service.StartAsync();

        harness.Camera.CaptureResults.Enqueue(
            CameraCaptureResult.Unavailable(CameraFailureKind.Disconnected, "unplugged"));

        var first = await harness.Service.CaptureAsync();

        first.Failure.Should().Be(CameraFailureKind.Disconnected);
        harness.Service.State.Should().Be(CameraSessionState.Unavailable);
        harness.Service.LastFailure.Should().Be(CameraFailureKind.Disconnected);
        harness.Camera.IsOpen.Should().BeFalse();
        harness.Camera.CloseCalls.Should().Be(1);

        var gated = await harness.Service.CaptureAsync();
        gated.Failure.Should().Be(CameraFailureKind.Disconnected);
        harness.Camera.OpenRequests.Should().ContainSingle();

        harness.Clock.Advance(TimeSpan.FromSeconds(30));
        harness.Camera.OpenResults.Enqueue(CameraOpenResult.Opened("cam-a"));

        var recovered = await harness.Service.CaptureAsync();

        recovered.Captured.Should().BeTrue();
        harness.Camera.OpenRequests.Should().HaveCount(2);
        harness.Service.State.Should().Be(CameraSessionState.Active);
    }

    [Fact]
    public async Task Capture_failures_below_the_threshold_keep_the_session_active()
    {
        var harness = new Harness();
        var service = harness.Create(new CameraOptions { MaxConsecutiveCaptureFailures = 3 });
        await service.StartAsync();

        harness.Camera.CaptureResults.Enqueue(
            CameraCaptureResult.Unavailable(CameraFailureKind.Unknown, "frame-read-failed"));
        harness.Camera.CaptureResults.Enqueue(
            CameraCaptureResult.Unavailable(CameraFailureKind.Unknown, "frame-read-failed"));

        (await service.CaptureAsync()).Captured.Should().BeFalse();
        (await service.CaptureAsync()).Captured.Should().BeFalse();

        service.State.Should().Be(CameraSessionState.Active);
        harness.Camera.CloseCalls.Should().Be(0);

        var success = await service.CaptureAsync();
        success.Captured.Should().BeTrue();

        harness.Camera.CaptureResults.Enqueue(
            CameraCaptureResult.Unavailable(CameraFailureKind.Unknown, "frame-read-failed"));
        harness.Camera.CaptureResults.Enqueue(
            CameraCaptureResult.Unavailable(CameraFailureKind.Unknown, "frame-read-failed"));

        (await service.CaptureAsync()).Captured.Should().BeFalse();
        (await service.CaptureAsync()).Captured.Should().BeFalse();

        service.State.Should().Be(CameraSessionState.Active);
        harness.Camera.CloseCalls.Should().Be(0);
    }

    [Fact]
    public async Task Capture_failures_reaching_the_threshold_mark_the_camera_unavailable()
    {
        var harness = new Harness();
        var service = harness.Create(new CameraOptions { MaxConsecutiveCaptureFailures = 2 });
        await service.StartAsync();

        harness.Camera.CaptureResults.Enqueue(
            CameraCaptureResult.Unavailable(CameraFailureKind.Unknown, "frame-read-failed"));
        harness.Camera.CaptureResults.Enqueue(
            CameraCaptureResult.Unavailable(CameraFailureKind.Unknown, "frame-read-failed"));

        await service.CaptureAsync();
        service.State.Should().Be(CameraSessionState.Active);

        await service.CaptureAsync();

        service.State.Should().Be(CameraSessionState.Unavailable);
        harness.Camera.IsOpen.Should().BeFalse();
        harness.Camera.CloseCalls.Should().Be(1);
    }

    [Fact]
    public async Task A_blocking_capture_that_exceeds_the_timeout_is_reported_as_a_failure()
    {
        var harness = new Harness();
        var service = harness.Create(new CameraOptions
        {
            OperationTimeout = TimeSpan.FromMilliseconds(20),
            MaxConsecutiveCaptureFailures = 1
        });
        await service.StartAsync();

        harness.Camera.CaptureHandler = () =>
        {
            Thread.Sleep(200);
            return CameraCaptureResult.Unavailable(CameraFailureKind.Unknown, "late");
        };

        var result = await service.CaptureAsync();

        result.Captured.Should().BeFalse();
        result.Failure.Should().Be(CameraFailureKind.Timeout);
        service.State.Should().Be(CameraSessionState.Unavailable);
        harness.Camera.CloseCalls.Should().Be(1);
    }

    [Fact]
    public async Task A_blocking_open_that_exceeds_the_timeout_is_reported_and_retried_later()
    {
        var harness = new Harness();
        var service = harness.Create(new CameraOptions
        {
            OperationTimeout = TimeSpan.FromMilliseconds(20)
        });
        harness.Camera.OpenDelay = () => Thread.Sleep(200);

        var result = await service.StartAsync();

        result.Success.Should().BeFalse();
        result.Failure.Should().Be(CameraFailureKind.Timeout);
        service.State.Should().Be(CameraSessionState.Unavailable);

        var gated = await service.CaptureAsync();
        gated.Failure.Should().Be(CameraFailureKind.Timeout);
        harness.Camera.OpenRequests.Should().ContainSingle();
        harness.Camera.CaptureCalls.Should().Be(0);
    }

    [Fact]
    public async Task Stop_closes_the_camera_and_returns_to_off()
    {
        var harness = new Harness();
        await harness.Service.StartAsync();

        await harness.Service.StopAsync();

        harness.Service.State.Should().Be(CameraSessionState.Off);
        harness.Service.ActiveDeviceId.Should().BeNull();
        harness.Service.LastFailure.Should().Be(CameraFailureKind.NotStarted);
        harness.Camera.IsOpen.Should().BeFalse();
        harness.Camera.CloseCalls.Should().Be(1);

        var result = await harness.Service.CaptureAsync();
        result.Failure.Should().Be(CameraFailureKind.NotStarted);
        harness.Camera.CaptureCalls.Should().Be(0);
    }

    [Fact]
    public async Task Stop_before_start_is_safe()
    {
        var harness = new Harness();

        await harness.Service.StopAsync();

        harness.Service.State.Should().Be(CameraSessionState.Off);
        harness.Service.LastFailure.Should().Be(CameraFailureKind.NotStarted);
    }

    [Fact]
    public async Task State_changed_is_raised_for_active_and_off_transitions()
    {
        var harness = new Harness();

        await harness.Service.StartAsync();
        await harness.Service.StopAsync();

        harness.States.Should().Equal(CameraSessionState.Active, CameraSessionState.Off);
    }

    [Fact]
    public async Task Refresh_devices_returns_the_catalog_and_survives_catalog_failures()
    {
        var harness = new Harness();
        harness.Camera.Devices = [Device("cam-a", 0), Device("cam-b", 1)];

        var devices = await harness.Service.RefreshDevicesAsync();

        devices.Should().Equal(harness.Camera.Devices);
        harness.Service.Devices.Should().Equal(harness.Camera.Devices);

        harness.Camera.ThrowOnList = true;

        var failed = await harness.Service.RefreshDevicesAsync();

        failed.Should().Equal(harness.Camera.Devices);
        harness.Service.Devices.Should().Equal(harness.Camera.Devices);
        harness.Logger.Errors.Should().Contain(
            message => message.Contains("Could not list camera devices"));
    }

    public static IEnumerable<object[]> InvalidOptions()
    {
        yield return [new CameraOptions { OperationTimeout = TimeSpan.Zero }];
        yield return [new CameraOptions { OperationTimeout = TimeSpan.FromMinutes(2) }];
        yield return [new CameraOptions { InitialRetryDelay = TimeSpan.Zero }];
        yield return [new CameraOptions { MaxRetryDelay = TimeSpan.FromSeconds(10) }];
        yield return [new CameraOptions { MaxConsecutiveCaptureFailures = 0 }];
        yield return [new CameraOptions { MaxConsecutiveCaptureFailures = 101 }];
    }

    [Theory]
    [MemberData(nameof(InvalidOptions))]
    public void Constructor_rejects_invalid_options(CameraOptions options)
    {
        var harness = new Harness();

        var act = () => new CameraSessionService(
            harness.Camera,
            harness.Settings,
            harness.Logger,
            harness.Clock,
            options);

        act.Should().Throw<ArgumentOutOfRangeException>();
    }

    private static AppSettings SettingsWithCamera(string? deviceId)
    {
        var settings = AppSettings.CreateDefault(Now);
        settings.SetCameraDevice(deviceId, Now);
        return settings;
    }

    private static CameraDeviceInfo Device(string id, int index) => new(id, $"Camera {index}", index);

    private sealed class Harness
    {
        public ManualTimeProvider Clock { get; } = new(Now);

        public FakeCameraPort Camera { get; } = new();

        public FakeAppSettingsRepository Settings { get; } = new();

        public RecordingLogger Logger { get; } = new();

        public List<CameraSessionState> States { get; } = [];

        public CameraSessionService Service { get; }

        public Harness()
        {
            Service = new CameraSessionService(
                Camera,
                Settings,
                Logger,
                Clock,
                new CameraOptions());

            Service.StateChanged += (_, _) => States.Add(Service.State);
        }

        public CameraSessionService Create(CameraOptions options)
        {
            var service = new CameraSessionService(Camera, Settings, Logger, Clock, options);
            service.StateChanged += (_, _) => States.Add(service.State);
            return service;
        }
    }
}
