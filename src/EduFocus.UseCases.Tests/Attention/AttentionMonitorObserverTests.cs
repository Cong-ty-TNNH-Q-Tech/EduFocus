using EduFocus.Domain.Attention;
using EduFocus.Domain.Entities;
using EduFocus.Domain.Enums;
using EduFocus.Ports.DTOs;
using EduFocus.UseCases.Attention;
using EduFocus.UseCases.Tests.TestSupport;
using FluentAssertions;

namespace EduFocus.UseCases.Tests.Attention;

public sealed class AttentionMonitorObserverTests
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

    [Fact]
    public async Task Observers_see_the_counted_state_for_each_sample()
    {
        await using var harness = new Harness();
        await harness.Monitor.StartAsync(harness.Session.Id);

        await harness.SampleAsync(second: 0);
        await harness.SampleAsync(second: 1);
        await harness.SampleAsync(second: 2);

        harness.Observer.Observations.Should().HaveCount(2);
        harness.Observer.Observations[0].State.Should().BeNull();
        harness.Observer.Observations[1].State.Should().Be(AttentionState.Focused);
        harness.Observer.Observations[1].ObservedAtUtc.Should().Be(Start.AddSeconds(2));
    }

    [Fact]
    public async Task Stop_reports_a_null_state()
    {
        await using var harness = new Harness();
        await harness.Monitor.StartAsync(harness.Session.Id);
        await harness.SampleAsync(second: 0);
        await harness.SampleAsync(second: 1);
        await harness.SampleAsync(second: 2);

        await harness.Monitor.StopAsync();

        harness.Observer.Observations.Should().HaveCount(3);
        harness.Observer.Observations[^1].State.Should().BeNull();
    }

    [Fact]
    public async Task Failing_observers_do_not_disturb_counting()
    {
        await using var harness = new Harness();
        harness.Observer.Throw = true;
        await harness.Monitor.StartAsync(harness.Session.Id);

        await harness.SampleAsync(second: 0);
        await harness.SampleAsync(second: 1);
        (await harness.SampleAsync(second: 2)).Should().BeTrue();
        (await harness.SampleAsync(second: 3)).Should().BeTrue();

        harness.Session.FocusedSeconds.Should().Be(2);
        harness.Logger.Warnings.Should().Contain(message => message.Contains("observer failed"));
    }

    private sealed class Harness : IAsyncDisposable
    {
        public Harness()
        {
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
                AttentionMonitoringOptions.Default,
                new AttentionThresholds(),
                [Observer]);
        }

        public FakeAppSettingsRepository Settings { get; } = new();

        public FakeAttentionEventRepository Events { get; } = new();

        public FakeStudySessionRepository Sessions { get; } = new();

        public FakeAttentionPipelineUseCase Pipeline { get; } = new();

        public RecordingLogger Logger { get; } = new();

        public ManualTimeProvider Clock { get; } = new(Start);

        public RecordingAttentionStateObserver Observer { get; } = new();

        public StudySession Session { get; }

        public AttentionMonitorService Monitor { get; }

        public Task<bool> SampleAsync(int second) =>
            Monitor.ProcessAsync(new AttentionSample(
                Start.AddSeconds(second),
                CameraFailureKind.None,
                Frontal,
                120,
                0,
                0,
                null));

        public async ValueTask DisposeAsync() => await Monitor.DisposeAsync();
    }
}
