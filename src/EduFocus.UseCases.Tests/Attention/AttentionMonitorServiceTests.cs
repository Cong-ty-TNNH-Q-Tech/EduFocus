using EduFocus.Domain.Attention;
using EduFocus.Domain.Entities;
using EduFocus.Domain.Enums;
using EduFocus.Ports.DTOs;
using EduFocus.UseCases.Attention;
using EduFocus.UseCases.Tests.TestSupport;
using FluentAssertions;
using Xunit;

namespace EduFocus.UseCases.Tests.Attention;

public class AttentionMonitorServiceTests
{
    private static readonly DateTimeOffset Start = new(2026, 10, 30, 8, 0, 0, TimeSpan.Zero);

    private static FaceObservation Frontal => new(
        CameraAvailable: true,
        FacePresent: true,
        FaceCount: 1,
        Confidence: 0.9f,
        X: 200,
        Y: 150,
        Width: 200,
        Height: 200,
        RightEye: new Landmark(270, 220),
        LeftEye: new Landmark(330, 220),
        Nose: new Landmark(300, 260),
        RightMouth: new Landmark(275, 300),
        LeftMouth: new Landmark(325, 300),
        EyeCount: 2,
        HeadYawRatio: 0.10,
        HeadPitchRatio: 0.05,
        LandmarksAvailable: true);

    private static FaceObservation Turned => Frontal with { HeadYawRatio = 0.80 };

    private static FaceObservation EyesClosed => Frontal with { EyesClosed = true };

    [Fact]
    public async Task First_event_opens_only_after_the_window_has_a_majority()
    {
        await using var h = new Harness();
        await h.StartAsync();

        (await h.SampleAsync(0, Frontal)).Should().BeFalse();
        (await h.SampleAsync(1, Frontal)).Should().BeFalse();
        h.Events.Events.Should().BeEmpty();

        (await h.SampleAsync(2, Frontal)).Should().BeTrue();

        h.Events.Events.Should().ContainSingle();
        var opened = h.Events.Events[0];
        opened.State.Should().Be(AttentionState.Focused);
        opened.StartedAtUtc.Should().Be(Start.AddSeconds(2).UtcDateTime);
        opened.IsOpen.Should().BeTrue();
        opened.Confidence.Should().BeApproximately(0.9, 0.0001);
        opened.Note.Should().Be("frontal");
        h.Session.MonitoredSeconds.Should().Be(1);
        h.Session.FocusedSeconds.Should().Be(1);
        h.Session.ConfidenceAvg.Should().BeApproximately(0.9, 0.0001);
    }

    [Fact]
    public async Task Stable_state_does_not_open_duplicate_events()
    {
        await using var h = new Harness();
        await h.StartAsync();
        await h.SampleAsync(0, Frontal);
        await h.SampleAsync(1, Frontal);
        await h.SampleAsync(2, Frontal);

        await h.SampleAsync(3, Frontal);
        await h.SampleAsync(4, Frontal);

        h.Events.Events.Should().ContainSingle();
        h.Session.FocusedSeconds.Should().Be(3);
    }

    [Fact]
    public async Task Brief_noise_below_the_majority_does_not_change_the_event()
    {
        await using var h = new Harness();
        await h.StartAsync();
        await h.SampleAsync(0, Frontal);
        await h.SampleAsync(1, Frontal);
        await h.SampleAsync(2, Frontal);

        await h.SampleAsync(3, Turned);
        await h.SampleAsync(4, Frontal);
        await h.SampleAsync(5, Frontal);

        h.Events.Events.Should().ContainSingle();
        h.Events.Events[0].State.Should().Be(AttentionState.Focused);
        h.Monitor.CurrentState.Should().Be(AttentionState.Focused);
    }

    [Fact]
    public async Task Sustained_distraction_closes_focus_and_opens_a_distracted_event()
    {
        await using var h = new Harness();
        await h.StartAsync();
        await h.SampleAsync(0, Frontal);
        await h.SampleAsync(1, Frontal);
        await h.SampleAsync(2, Frontal);

        for (var second = 3; second <= 27; second++)
            await h.SampleAsync(second, Turned);

        h.Events.Events.Should().HaveCount(2);
        var focused = h.Events.Events[0];
        focused.State.Should().Be(AttentionState.Focused);
        focused.IsOpen.Should().BeFalse();
        focused.EndedAtUtc.Should().Be(Start.AddSeconds(27).UtcDateTime);
        focused.DurationSeconds.Should().Be(25);

        var distracted = h.Events.Events[1];
        distracted.State.Should().Be(AttentionState.Distracted);
        distracted.StartedAtUtc.Should().Be(Start.AddSeconds(27).UtcDateTime);
        distracted.IsOpen.Should().BeTrue();
        h.Session.FocusedSeconds.Should().Be(25);
        h.Session.DistractedSeconds.Should().Be(1);
    }

    [Fact]
    public async Task Thresholds_are_loaded_from_settings_when_the_session_starts()
    {
        var settings = AppSettings.CreateDefault(Start.UtcDateTime);
        settings.UpdateThresholds(5, 5, 15, Start.UtcDateTime);

        await using var h = new Harness(settings: settings);
        await h.StartAsync();
        await h.SampleAsync(0, Frontal);
        await h.SampleAsync(1, Frontal);
        await h.SampleAsync(2, Frontal);

        for (var second = 3; second <= 15; second++)
            await h.SampleAsync(second, Turned);

        h.Events.Events.Should().Contain(x => x.State == AttentionState.Distracted);
        h.Session.DistractedSeconds.Should().BeGreaterThan(0);
    }

    [Fact]
    public async Task Drowsy_closes_focus_after_the_drowsy_threshold()
    {
        await using var h = new Harness();
        await h.StartAsync();
        await h.SampleAsync(0, Frontal);
        await h.SampleAsync(1, Frontal);
        await h.SampleAsync(2, Frontal);

        for (var second = 3; second <= 9; second++)
            await h.SampleAsync(second, EyesClosed);

        h.Events.Events.Should().HaveCount(2);
        var focused = h.Events.Events[0];
        focused.State.Should().Be(AttentionState.Focused);
        focused.EndedAtUtc.Should().Be(Start.AddSeconds(9).UtcDateTime);

        var drowsy = h.Events.Events[1];
        drowsy.State.Should().Be(AttentionState.Drowsy);
        drowsy.StartedAtUtc.Should().Be(Start.AddSeconds(9).UtcDateTime);
        drowsy.IsOpen.Should().BeTrue();
        drowsy.Note.Should().Be("eyes-closed");
        h.Session.DrowsySeconds.Should().Be(1);
    }

    [Fact]
    public async Task Camera_failure_opens_an_event_immediately_and_excludes_attention_time()
    {
        await using var h = new Harness();
        await h.StartAsync();

        (await h.CameraFailureAsync(0)).Should().BeTrue();

        h.Events.Events.Should().ContainSingle();
        var camera = h.Events.Events[0];
        camera.State.Should().Be(AttentionState.CameraUnavailable);
        camera.StartedAtUtc.Should().Be(Start.UtcDateTime);
        camera.Confidence.Should().BeNull();
        camera.Note.Should().Be("camera-Disconnected");
        h.Session.MonitoredSeconds.Should().Be(0);
        h.Monitor.CurrentState.Should().Be(AttentionState.CameraUnavailable);

        await h.SampleAsync(1, Frontal);
        await h.SampleAsync(2, Frontal);
        (await h.SampleAsync(3, Frontal)).Should().BeTrue();

        h.Events.Events.Should().HaveCount(2);
        camera.IsOpen.Should().BeFalse();
        camera.EndedAtUtc.Should().Be(Start.AddSeconds(3).UtcDateTime);
        camera.DurationSeconds.Should().Be(3);
        h.Events.Events[1].State.Should().Be(AttentionState.Focused);
        h.Session.FocusedSeconds.Should().Be(1);
    }

    [Fact]
    public async Task A_busy_camera_never_creates_focused_or_distracted_time()
    {
        await using var h = new Harness();
        await h.StartAsync();
        h.Session.AddMonitoringEligibleSeconds(10, Start.UtcDateTime);

        for (var second = 0; second <= 5; second++)
        {
            await h.Monitor.ProcessAsync(new AttentionSample(
                Start.AddSeconds(second),
                CameraFailureKind.Busy,
                Observation: null,
                Luminance: 0,
                CaptureMs: 0,
                DetectMs: 0,
                Error: "camera busy"));
        }

        await h.Monitor.StopAsync();

        h.Events.Events.Should().ContainSingle();
        h.Events.Events[0].State.Should().Be(AttentionState.CameraUnavailable);
        h.Session.FocusedSeconds.Should().Be(0);
        h.Session.DistractedSeconds.Should().Be(0);
        h.Session.AbsentSeconds.Should().Be(0);
        h.Session.DrowsySeconds.Should().Be(0);
        h.Session.MonitoredSeconds.Should().Be(0);
        h.Session.CameraUnavailableSeconds.Should().Be(5);

        var summary = SessionAttentionSummary.From(h.Session);
        summary.CoveragePercent.Should().Be(0);
        summary.CameraUnavailableSeconds.Should().Be(5);
        summary.UnobservedSeconds.Should().Be(10);
        summary.IsCoverageLow.Should().BeTrue();
    }

    [Fact]
    public async Task Inference_errors_are_not_counted_as_absence()
    {
        await using var h = new Harness();
        await h.StartAsync();
        await h.SampleAsync(0, Frontal);
        await h.SampleAsync(1, Frontal);
        await h.SampleAsync(2, Frontal);

        (await h.InferenceErrorAsync(3)).Should().BeFalse();
        (await h.InferenceErrorAsync(4)).Should().BeFalse();

        h.Events.Events.Should().ContainSingle();
        h.Monitor.CurrentState.Should().Be(AttentionState.Focused);

        (await h.SampleAsync(5, Frontal)).Should().BeTrue();
        h.Events.Events.Should().ContainSingle();
        h.Session.AbsentSeconds.Should().Be(0);
    }

    [Fact]
    public async Task Elapsed_time_beyond_the_max_gap_is_not_counted()
    {
        await using var h = new Harness();
        await h.StartAsync();
        await h.SampleAsync(0, Frontal);
        await h.SampleAsync(1, Frontal);
        await h.SampleAsync(2, Frontal);
        var before = h.Session.FocusedSeconds;

        await h.SampleAsync(20, Frontal);
        h.Session.FocusedSeconds.Should().Be(before);

        await h.SampleAsync(21, Frontal);
        h.Session.FocusedSeconds.Should().Be(before + 1);
    }

    [Fact]
    public async Task Persisted_attention_time_feeds_the_session_summary_with_coverage()
    {
        await using var h = new Harness();
        await h.StartAsync();
        h.Session.AddMonitoringEligibleSeconds(10, Start.UtcDateTime);

        for (var second = 0; second <= 5; second++)
            await h.SampleAsync(second, Frontal);

        await h.Monitor.StopAsync();

        var summary = SessionAttentionSummary.From(h.Session);
        summary.FocusedSeconds.Should().Be(4);
        summary.MonitoredSeconds.Should().Be(4);
        summary.AttentionPercent.Should().Be(100);
        summary.CoveragePercent.Should().Be(40);
        summary.IsCoverageLow.Should().BeTrue();
        summary.Warning.Should().Be(SessionAttentionSummary.LowCoverageWarning);
        h.Events.Events.Should().ContainSingle()
            .Which.IsOpen.Should().BeFalse();
    }

    [Fact]
    public async Task Stop_closes_the_open_event_and_unsubscribes()
    {
        await using var h = new Harness();
        await h.StartAsync();
        await h.SampleAsync(0, Frontal);
        await h.SampleAsync(1, Frontal);
        await h.SampleAsync(2, Frontal);

        await h.Monitor.StopAsync();

        h.Monitor.IsRunning.Should().BeFalse();
        h.Monitor.CurrentState.Should().BeNull();
        h.Events.Events.Should().ContainSingle();
        h.Events.Events[0].IsOpen.Should().BeFalse();
        h.Events.Events[0].EndedAtUtc.Should().Be(Start.AddSeconds(2).UtcDateTime);

        (await h.SampleAsync(3, Frontal)).Should().BeFalse();
        h.Events.Events.Should().ContainSingle();
    }

    [Fact]
    public async Task Restarting_closes_the_previous_event_before_reusing_the_monitor()
    {
        await using var h = new Harness();
        await h.StartAsync();
        await h.SampleAsync(0, Frontal);
        await h.SampleAsync(1, Frontal);
        await h.SampleAsync(2, Frontal);

        await h.StartAsync();

        h.Events.Events.Should().ContainSingle();
        h.Events.Events[0].IsOpen.Should().BeFalse();
        h.Monitor.CurrentState.Should().BeNull();
        h.Monitor.IsRunning.Should().BeTrue();
    }

    [Fact]
    public async Task Session_no_longer_active_stops_the_monitor_and_closes_the_event()
    {
        await using var h = new Harness();
        await h.StartAsync();
        await h.SampleAsync(0, Frontal);
        await h.SampleAsync(1, Frontal);
        await h.SampleAsync(2, Frontal);

        h.Session.Complete(Start.AddSeconds(3).UtcDateTime);

        (await h.SampleAsync(3, Frontal)).Should().BeFalse();

        h.Monitor.IsRunning.Should().BeFalse();
        h.Events.Events.Should().ContainSingle();
        h.Events.Events[0].IsOpen.Should().BeFalse();
        h.Events.Events[0].EndedAtUtc.Should().Be(Start.AddSeconds(3).UtcDateTime);
        h.Logger.Warnings.Should().Contain(message => message.Contains("no longer active"));
    }

    [Fact]
    public async Task Counter_failures_are_logged_once_and_do_not_block_state_events()
    {
        await using var h = new Harness();
        await h.StartAsync();
        h.Sessions.ThrowOnAttention = true;

        await h.SampleAsync(0, Frontal);
        await h.SampleAsync(1, Frontal);
        (await h.SampleAsync(2, Frontal)).Should().BeTrue();

        h.Events.Events.Should().ContainSingle();
        h.Logger.Warnings.Should().ContainSingle(message => message.Contains("Could not persist attention time"));

        await h.SampleAsync(3, Frontal);
        h.Logger.Warnings.Should().ContainSingle(message => message.Contains("Could not persist attention time"));

        h.Sessions.ThrowOnAttention = false;
        await h.SampleAsync(4, Frontal);
        h.Session.FocusedSeconds.Should().Be(1);
    }

    [Fact]
    public async Task A_failed_event_write_is_retried_on_the_next_sample()
    {
        await using var h = new Harness();
        await h.StartAsync();
        h.Events.ThrowOnAdd = true;

        await h.SampleAsync(0, Frontal);
        await h.SampleAsync(1, Frontal);
        (await h.SampleAsync(2, Frontal)).Should().BeFalse();
        h.Events.Events.Should().BeEmpty();

        h.Events.ThrowOnAdd = false;
        (await h.SampleAsync(3, Frontal)).Should().BeTrue();

        h.Events.Events.Should().ContainSingle();
        h.Events.Events[0].State.Should().Be(AttentionState.Focused);
        h.Events.Events[0].StartedAtUtc.Should().Be(Start.AddSeconds(3).UtcDateTime);
    }

    [Fact]
    public async Task A_failed_event_close_update_is_retried_without_throwing_already_closed()
    {
        await using var h = new Harness();
        await h.StartAsync();
        await h.SampleAsync(0, Frontal);
        await h.SampleAsync(1, Frontal);
        await h.SampleAsync(2, Frontal);

        h.Events.ThrowOnUpdate = true;
        (await h.CameraFailureAsync(3)).Should().BeFalse();

        h.Events.ThrowOnUpdate = false;
        (await h.CameraFailureAsync(4)).Should().BeTrue();

        h.Events.Events.Should().HaveCount(2);
        h.Events.Events[0].State.Should().Be(AttentionState.Focused);
        h.Events.Events[0].IsOpen.Should().BeFalse();
        h.Events.Events[0].EndedAtUtc.Should().Be(Start.AddSeconds(3).UtcDateTime);
        h.Events.Events[1].State.Should().Be(AttentionState.CameraUnavailable);
        h.Events.Events[1].IsOpen.Should().BeTrue();
    }

    [Fact]
    public async Task Sleep_gap_beyond_max_sample_gap_closes_open_event_at_pre_sleep_timestamp()
    {
        await using var h = new Harness();
        await h.StartAsync();
        await h.SampleAsync(0, Frontal);
        await h.SampleAsync(1, Frontal);
        await h.SampleAsync(2, Frontal);

        await h.SampleAsync(3600, Frontal);
        await h.SampleAsync(3601, Frontal);
        await h.SampleAsync(3602, Frontal);

        h.Events.Events.Should().HaveCount(2);
        h.Events.Events[0].EndedAtUtc.Should().Be(Start.AddSeconds(2).UtcDateTime);
        h.Events.Events[0].DurationSeconds.Should().Be(0);
        h.Events.Events[1].StartedAtUtc.Should().Be(Start.AddSeconds(3602).UtcDateTime);
    }

    [Fact]
    public async Task Pipeline_samples_are_processed_while_running()
    {
        await using var h = new Harness();
        await h.StartAsync();

        h.Pipeline.Raise(SampleAt(0, Frontal));
        h.Pipeline.Raise(SampleAt(1, Frontal));
        h.Pipeline.Raise(SampleAt(2, Frontal));

        await WaitUntilAsync(() => h.Events.Events.Count == 1);

        h.Events.Events[0].State.Should().Be(AttentionState.Focused);
        h.Events.Events[0].IsOpen.Should().BeTrue();
    }

    [Fact]
    public async Task Process_before_start_returns_false()
    {
        await using var h = new Harness();

        (await h.SampleAsync(0, Frontal)).Should().BeFalse();
        h.Events.Events.Should().BeEmpty();
    }

    [Fact]
    public void Invalid_options_are_rejected()
    {
        var invalid = new AttentionMonitoringOptions[]
        {
            new(TimeSpan.FromSeconds(4), 3, 0.6, TimeSpan.FromSeconds(5)),
            new(TimeSpan.FromSeconds(11), 3, 0.6, TimeSpan.FromSeconds(5)),
            new(TimeSpan.FromSeconds(6), 1, 0.6, TimeSpan.FromSeconds(5)),
            new(TimeSpan.FromSeconds(6), 3, 0.4, TimeSpan.FromSeconds(5)),
            new(TimeSpan.FromSeconds(6), 3, 0.6, TimeSpan.Zero)
        };

        foreach (var options in invalid)
        {
            var create = () => new AttentionMonitorService(
                new FakeAttentionEventRepository(),
                new FakeStudySessionRepository(),
                new FakeAttentionPipelineUseCase(),
                new FakeAppSettingsRepository(),
                new RecordingLogger(),
                new ManualTimeProvider(Start),
                options,
                new AttentionThresholds());

            create.Should().Throw<ArgumentOutOfRangeException>();
        }
    }

    private static AttentionSample SampleAt(int second, FaceObservation observation) =>
        new(Start.AddSeconds(second), CameraFailureKind.None, observation, 120, 0, 0, null);

    private static async Task WaitUntilAsync(Func<bool> condition)
    {
        var deadline = DateTime.UtcNow.AddSeconds(5);
        while (DateTime.UtcNow < deadline)
        {
            if (condition())
                return;

            await Task.Delay(10);
        }

        throw new TimeoutException("The attention monitor did not process the samples in time.");
    }

    private sealed class Harness : IAsyncDisposable
    {
        public Harness(
            AttentionMonitoringOptions? options = null,
            AttentionThresholds? thresholds = null,
            AppSettings? settings = null)
        {
            Settings.Stored = settings;
            Session = StudySession.Start(
                Start.UtcDateTime,
                new DateOnly(2026, 10, 30),
                SessionTrigger.Manual,
                monitoringEnabled: true);

            Sessions.Sessions.Add(Session);
            Monitor = new AttentionMonitorService(
                Events,
                Sessions,
                Pipeline,
                Settings,
                Logger,
                Clock,
                options ?? AttentionMonitoringOptions.Default,
                thresholds ?? new AttentionThresholds());
        }

        public FakeAppSettingsRepository Settings { get; } = new();

        public FakeAttentionEventRepository Events { get; } = new();

        public FakeStudySessionRepository Sessions { get; } = new();

        public FakeAttentionPipelineUseCase Pipeline { get; } = new();

        public RecordingLogger Logger { get; } = new();

        public ManualTimeProvider Clock { get; } = new(Start);

        public StudySession Session { get; }

        public AttentionMonitorService Monitor { get; }

        public Task StartAsync() => Monitor.StartAsync(Session.Id);

        public Task<bool> SampleAsync(int second, FaceObservation observation) =>
            Monitor.ProcessAsync(SampleAt(second, observation));

        public Task<bool> CameraFailureAsync(int second) =>
            Monitor.ProcessAsync(new AttentionSample(
                Start.AddSeconds(second),
                CameraFailureKind.Disconnected,
                null,
                0,
                0,
                0,
                "Disconnected"));

        public Task<bool> InferenceErrorAsync(int second) =>
            Monitor.ProcessAsync(new AttentionSample(
                Start.AddSeconds(second),
                CameraFailureKind.None,
                null,
                120,
                0,
                0,
                "inference failed"));

        public async ValueTask DisposeAsync() => await Monitor.DisposeAsync();
    }
}
