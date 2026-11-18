using System.Text.Json;
using EduFocus.Domain.Reports;
using EduFocus.Ports.Outbound.Cloud;
using EduFocus.UseCases.Cloud;
using EduFocus.UseCases.Tests.TestSupport;
using FluentAssertions;

namespace EduFocus.UseCases.Tests.Cloud;

public sealed class CloudDailySummaryProducerTests
{
    private static readonly DateTimeOffset Now = new(2026, 11, 17, 20, 30, 0, TimeSpan.Zero);
    private static readonly DateOnly LocalDate = new(2026, 11, 18);
    private static readonly TimeZoneInfo UtcPlus7 = TimeZoneInfo.CreateCustomTimeZone(
        "EduFocus/Test-UTC7",
        TimeSpan.FromHours(7),
        "EduFocus Test UTC+7",
        "EduFocus Test UTC+7");

    [Fact]
    public async Task Publishes_the_whitelisted_aggregate_fields_for_the_local_day()
    {
        var harness = new Harness();
        harness.Sessions.DailySummary = Summary(LocalDate);

        var published = await harness.Producer.TryPublishAsync();

        published.Should().BeTrue();
        harness.Sessions.DailySummaryRequests.Should().ContainSingle().Which.Should().Be(LocalDate);

        var queued = harness.Sink.Enqueued.Should().ContainSingle().Subject;
        queued.Type.Should().Be(CloudEventTypes.DailySummary);
        queued.ClientEventId.Should().BeNull();

        var payload = queued.Payload;
        payload.EnumerateObject().Select(property => property.Name).Should().BeEquivalentTo(
        [
            "localDate",
            "timeZoneId",
            "generatedAtUtc",
            "sessionsCount",
            "activeSeconds",
            "studySeconds",
            "idleSeconds",
            "onlineClassSeconds",
            "monitoringEligibleSeconds",
            "monitoredSeconds",
            "focusedSeconds",
            "distractedSeconds",
            "absentSeconds",
            "drowsySeconds",
            "blockedAttempts",
            "attentionPercent",
            "coveragePercent",
        ]);
        payload.GetProperty("localDate").GetString().Should().Be("2026-11-18");
        payload.GetProperty("timeZoneId").ValueKind.Should().Be(JsonValueKind.Null);
        payload.GetProperty("generatedAtUtc").GetDateTimeOffset().Should().Be(Now);
        payload.GetProperty("sessionsCount").GetInt32().Should().Be(2);
        payload.GetProperty("activeSeconds").GetInt32().Should().Be(3_600);
        payload.GetProperty("idleSeconds").GetInt32().Should().Be(240);
        payload.GetProperty("blockedAttempts").GetInt32().Should().Be(4);
        payload.GetProperty("monitoredSeconds").GetInt32().Should().Be(2_280);
        payload.GetProperty("attentionPercent").GetDouble()
            .Should().BeApproximately(1_800 * 100.0 / 2_280, 0.0001);
        payload.GetProperty("coveragePercent").GetDouble()
            .Should().BeApproximately(2_280 * 100.0 / 2_400, 0.0001);
    }

    [Fact]
    public async Task Skips_days_without_sessions()
    {
        var harness = new Harness();
        harness.Sessions.DailySummary = new DailyStudySummary { LocalDate = LocalDate };

        var published = await harness.Producer.TryPublishAsync();

        published.Should().BeFalse();
        harness.Sink.Enqueued.Should().BeEmpty();
    }

    [Fact]
    public async Task Skips_when_the_summary_is_missing()
    {
        var harness = new Harness();

        var published = await harness.Producer.TryPublishAsync();

        published.Should().BeFalse();
        harness.Sink.Enqueued.Should().BeEmpty();
    }

    [Fact]
    public async Task Returns_false_when_the_sink_rejects_the_event()
    {
        var harness = new Harness();
        harness.Sessions.DailySummary = Summary(LocalDate);
        harness.Sink.Result = false;

        (await harness.Producer.TryPublishAsync()).Should().BeFalse();
    }

    [Fact]
    public async Task Repository_failures_are_logged_and_swallowed()
    {
        var harness = new Harness();
        harness.Sessions.ThrowOnDailySummary = true;

        (await harness.Producer.TryPublishAsync()).Should().BeFalse();

        harness.Logger.Warnings.Should().ContainSingle()
            .Which.Should().Contain(nameof(InvalidOperationException));
    }

    [Fact]
    public async Task Sink_failures_are_logged_and_swallowed()
    {
        var harness = new Harness();
        harness.Sessions.DailySummary = Summary(LocalDate);
        harness.Sink.Throw = true;

        (await harness.Producer.TryPublishAsync()).Should().BeFalse();

        harness.Logger.Warnings.Should().ContainSingle()
            .Which.Should().Contain(nameof(InvalidOperationException));
    }

    [Fact]
    public async Task Midnight_rollover_publishes_previous_day_summary()
    {
        var harness = new Harness();
        harness.Sessions.DailySummary = Summary(new DateOnly(2026, 11, 18));

        (await harness.Producer.TryPublishAsync()).Should().BeTrue();
        harness.Sink.Enqueued.Should().ContainSingle();

        // Advance past midnight (03:30 UTC+7 on 2026-11-18 -> 00:30 UTC+7 on 2026-11-19).
        harness.Time.Advance(TimeSpan.FromHours(21));

        (await harness.Producer.TryPublishAsync()).Should().BeTrue();
        harness.Sessions.DailySummaryRequests.Should().Equal(
            new DateOnly(2026, 11, 18),
            new DateOnly(2026, 11, 18),
            new DateOnly(2026, 11, 19));
    }

    private static DailyStudySummary Summary(DateOnly localDate) => new()
    {
        LocalDate = localDate,
        SessionCount = 2,
        ActiveSeconds = 3_600,
        StudySeconds = 3_000,
        IdleSeconds = 240,
        OnlineClassSeconds = 900,
        MonitoringEligibleSeconds = 2_400,
        FocusedSeconds = 1_800,
        DistractedSeconds = 300,
        AbsentSeconds = 120,
        DrowsySeconds = 60,
        BlockedAttemptCount = 4,
    };

    private sealed class Harness
    {
        public Harness()
        {
            Time = new ManualTimeProvider(Now, UtcPlus7);
            Producer = new CloudDailySummaryProducer(
                Sessions,
                Sink,
                Logger,
                Time);
        }

        public ManualTimeProvider Time { get; }

        public FakeStudySessionRepository Sessions { get; } = new();

        public RecordingCloudEventSink Sink { get; } = new();

        public RecordingLogger Logger { get; } = new();

        public CloudDailySummaryProducer Producer { get; }
    }
}
