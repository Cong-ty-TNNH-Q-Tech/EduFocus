using EduFocus.Domain.Attention;
using EduFocus.Ports.Outbound.Cloud;
using EduFocus.UseCases.Cloud;
using EduFocus.UseCases.Tests.TestSupport;
using FluentAssertions;

namespace EduFocus.UseCases.Tests.Cloud;

public sealed class CloudAttentionAlertProducerTests
{
    private static readonly DateTimeOffset Now = new(2026, 11, 17, 20, 0, 0, TimeSpan.Zero);
    private static readonly TimeZoneInfo UtcPlus7 = TimeZoneInfo.CreateCustomTimeZone(
        "EduFocus/Test-UTC7",
        TimeSpan.FromHours(7),
        "EduFocus Test UTC+7",
        "EduFocus Test UTC+7");

    [Fact]
    public async Task Streaks_under_five_minutes_do_not_alert()
    {
        var harness = new Harness();

        await harness.ObserveAsync(AttentionState.Distracted, minutes: 0);
        await harness.ObserveAsync(AttentionState.Distracted, minutes: 4);

        harness.Sink.Enqueued.Should().BeEmpty();
    }

    [Fact]
    public async Task Five_continuous_minutes_queue_a_distracted_alert()
    {
        var harness = new Harness();

        await harness.ObserveAsync(AttentionState.Distracted, minutes: 0);
        await harness.ObserveAsync(AttentionState.Distracted, minutes: 5);

        var queued = harness.Sink.Enqueued.Should().ContainSingle().Subject;
        queued.Type.Should().Be(CloudEventTypes.AttentionAlert);
        queued.ClientEventId.Should().BeNull();
        queued.Payload.EnumerateObject().Select(property => property.Name)
            .Should().BeEquivalentTo(["kind", "localDate", "startedAtUtc", "durationSeconds"]);
        queued.Payload.GetProperty("kind").GetString().Should().Be("distracted");
        queued.Payload.GetProperty("localDate").GetString().Should().Be("2026-11-18");
        queued.Payload.GetProperty("startedAtUtc").GetDateTimeOffset().Should().Be(Now);
        queued.Payload.GetProperty("durationSeconds").GetInt32().Should().Be(300);
    }

    [Fact]
    public async Task Absent_streaks_report_the_absent_kind()
    {
        var harness = new Harness();

        await harness.ObserveAsync(AttentionState.Absent, minutes: 0);
        await harness.ObserveAsync(AttentionState.Absent, minutes: 10);

        var queued = harness.Sink.Enqueued.Should().ContainSingle().Subject;
        queued.Payload.GetProperty("kind").GetString().Should().Be("absent");
        queued.Payload.GetProperty("durationSeconds").GetInt32().Should().Be(600);
    }

    [Fact]
    public async Task Updates_are_throttled_to_one_per_fifteen_minutes()
    {
        var harness = new Harness();

        await harness.ObserveAsync(AttentionState.Distracted, minutes: 0);
        await harness.ObserveAsync(AttentionState.Distracted, minutes: 5);
        await harness.ObserveAsync(AttentionState.Distracted, minutes: 19);
        await harness.ObserveAsync(AttentionState.Distracted, minutes: 20);

        harness.Sink.Enqueued.Should().HaveCount(2);
        harness.Sink.Enqueued[1].Payload.GetProperty("durationSeconds").GetInt32().Should().Be(1_200);
    }

    [Fact]
    public async Task Other_states_never_alert_however_long_they_last()
    {
        var harness = new Harness();

        await harness.ObserveAsync(AttentionState.Focused, minutes: 0);
        await harness.ObserveAsync(AttentionState.Focused, minutes: 30);
        await harness.ObserveAsync(AttentionState.Drowsy, minutes: 30);
        await harness.ObserveAsync(AttentionState.Drowsy, minutes: 60);
        await harness.ObserveAsync(AttentionState.CameraUnavailable, minutes: 60);
        await harness.ObserveAsync(AttentionState.CameraUnavailable, minutes: 90);

        harness.Sink.Enqueued.Should().BeEmpty();
    }

    [Fact]
    public async Task A_state_change_restarts_the_streak()
    {
        var harness = new Harness();

        await harness.ObserveAsync(AttentionState.Distracted, minutes: 0);
        await harness.ObserveAsync(AttentionState.Distracted, minutes: 4);
        await harness.ObserveAsync(AttentionState.Focused, minutes: 5);
        await harness.ObserveAsync(AttentionState.Distracted, minutes: 6);
        await harness.ObserveAsync(AttentionState.Distracted, minutes: 10);

        harness.Sink.Enqueued.Should().BeEmpty();

        await harness.ObserveAsync(AttentionState.Distracted, minutes: 12);

        harness.Sink.Enqueued.Should().ContainSingle()
            .Which.Payload.GetProperty("durationSeconds").GetInt32().Should().Be(360);
    }

    [Fact]
    public async Task A_pause_resets_the_streak()
    {
        var harness = new Harness();

        await harness.ObserveAsync(AttentionState.Distracted, minutes: 0);
        await harness.ObserveAsync(AttentionState.Distracted, minutes: 4);
        await harness.ObserveAsync(null, minutes: 5);
        await harness.ObserveAsync(AttentionState.Distracted, minutes: 6);
        await harness.ObserveAsync(AttentionState.Distracted, minutes: 10);

        harness.Sink.Enqueued.Should().BeEmpty();
    }

    [Fact]
    public async Task Sink_failures_are_logged_and_swallowed()
    {
        var harness = new Harness();
        harness.Sink.Throw = true;

        await harness.ObserveAsync(AttentionState.Distracted, minutes: 0);
        Func<Task> act = async () => await harness.ObserveAsync(AttentionState.Distracted, minutes: 5);

        await act.Should().NotThrowAsync();
        harness.Logger.Warnings.Should().ContainSingle()
            .Which.Should().Contain(nameof(InvalidOperationException));
    }

    private sealed class Harness
    {
        public Harness()
        {
            Producer = new CloudAttentionAlertProducer(
                Sink,
                Logger,
                new ManualTimeProvider(Now, UtcPlus7));
        }

        public RecordingCloudEventSink Sink { get; } = new();

        public RecordingLogger Logger { get; } = new();

        public CloudAttentionAlertProducer Producer { get; }

        public ValueTask ObserveAsync(AttentionState? state, int minutes) =>
            Producer.OnStateObservedAsync(state, Now.AddMinutes(minutes));
    }
}
