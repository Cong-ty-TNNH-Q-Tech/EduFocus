using EduFocus.Domain.Entities;
using EduFocus.Ports.Outbound.Cloud;
using EduFocus.Ports.Outbound.Persistence;
using EduFocus.UseCases.Cloud;
using EduFocus.UseCases.Tests.TestSupport;
using FluentAssertions;

namespace EduFocus.UseCases.Tests.Cloud;

public sealed class CloudBlockedAttemptProducerTests
{
    private static readonly DateTimeOffset Now = new(2026, 11, 17, 20, 0, 0, TimeSpan.Zero);
    private static readonly TimeZoneInfo UtcPlus7 = TimeZoneInfo.CreateCustomTimeZone(
        "EduFocus/Test-UTC7",
        TimeSpan.FromHours(7),
        "EduFocus Test UTC+7",
        "EduFocus Test UTC+7");

    [Fact]
    public async Task The_first_poll_only_establishes_the_cursor()
    {
        var harness = new Harness();
        harness.Attempts.LatestCreatedAtUtc = Now.UtcDateTime.AddMinutes(-1);

        var published = await harness.Producer.TryPublishAsync();

        published.Should().BeFalse();
        harness.Sink.Enqueued.Should().BeEmpty();
        harness.Attempts.Requests.Should().BeEmpty();
    }

    [Fact]
    public async Task A_new_web_attempt_queues_a_generic_alert()
    {
        var harness = new Harness();
        await harness.Producer.TryPublishAsync();

        harness.Attempts.LatestCreatedAtUtc = Now.UtcDateTime.AddMinutes(1);
        harness.Time.Advance(TimeSpan.FromMinutes(1));

        var published = await harness.Producer.TryPublishAsync();

        published.Should().BeTrue();
        var queued = harness.Sink.Enqueued.Should().ContainSingle().Subject;
        queued.Type.Should().Be(CloudEventTypes.BlockedAttempt);
        queued.ClientEventId.Should().BeNull();
        queued.Payload.EnumerateObject().Select(property => property.Name)
            .Should().BeEquivalentTo(["localDate"]);
        queued.Payload.GetProperty("localDate").GetString().Should().Be("2026-11-18");
    }

    [Fact]
    public async Task The_alert_uses_the_attempt_local_date_not_the_poll_time()
    {
        var harness = new Harness(
            new DateTimeOffset(2026, 11, 17, 16, 50, 0, TimeSpan.Zero));
        await harness.Producer.TryPublishAsync();

        harness.Attempts.LatestCreatedAtUtc =
            new DateTime(2026, 11, 17, 16, 59, 0, DateTimeKind.Utc);
        harness.Time.Advance(TimeSpan.FromMinutes(11));

        var published = await harness.Producer.TryPublishAsync();

        published.Should().BeTrue();
        harness.Sink.Enqueued.Should().ContainSingle()
            .Which.Payload.GetProperty("localDate").GetString().Should().Be("2026-11-17");
    }

    [Fact]
    public async Task Alerts_are_throttled_to_one_per_fifteen_minutes()
    {
        var harness = new Harness();
        await harness.Producer.TryPublishAsync();

        harness.Attempts.LatestCreatedAtUtc = Now.UtcDateTime.AddMinutes(1);
        harness.Time.Advance(TimeSpan.FromMinutes(1));
        await harness.Producer.TryPublishAsync();

        harness.Attempts.LatestCreatedAtUtc = Now.UtcDateTime.AddMinutes(2);
        harness.Time.Advance(TimeSpan.FromMinutes(5));
        (await harness.Producer.TryPublishAsync()).Should().BeFalse();

        harness.Attempts.LatestCreatedAtUtc = Now.UtcDateTime.AddMinutes(20);
        harness.Time.Advance(TimeSpan.FromMinutes(10));
        (await harness.Producer.TryPublishAsync()).Should().BeTrue();

        harness.Sink.Enqueued.Should().HaveCount(2);
    }

    [Fact]
    public async Task Sink_failures_are_logged_and_swallowed()
    {
        var harness = new Harness();
        await harness.Producer.TryPublishAsync();
        harness.Attempts.LatestCreatedAtUtc = Now.UtcDateTime.AddMinutes(1);
        harness.Time.Advance(TimeSpan.FromMinutes(1));
        harness.Sink.Throw = true;

        Func<Task> act = async () => await harness.Producer.TryPublishAsync();

        await act.Should().NotThrowAsync();
        harness.Logger.Warnings.Should().ContainSingle()
            .Which.Should().Contain(nameof(InvalidOperationException));
    }

    private sealed class Harness
    {
        public Harness(DateTimeOffset? now = null)
        {
            Time = new ManualTimeProvider(now ?? Now, UtcPlus7);
            Producer = new CloudBlockedAttemptProducer(
                Attempts,
                Sink,
                Logger,
                Time);
        }

        public FakeBlockedAttemptRepository Attempts { get; } = new();

        public RecordingCloudEventSink Sink { get; } = new();

        public RecordingLogger Logger { get; } = new();

        public ManualTimeProvider Time { get; }

        public CloudBlockedAttemptProducer Producer { get; }
    }

    private sealed class FakeBlockedAttemptRepository : IBlockedAttemptRepository
    {
        public DateTime? LatestCreatedAtUtc { get; set; }

        public List<DateTime> Requests { get; } = [];

        public Task AddAsync(BlockedAttempt attempt, CancellationToken cancellationToken = default) =>
            Task.CompletedTask;

        public Task<DateTime?> GetLatestWebAttemptCreatedAtUtcAsync(
            DateTime afterUtc,
            CancellationToken cancellationToken = default)
        {
            Requests.Add(afterUtc);
            return Task.FromResult(
                LatestCreatedAtUtc is { } latest && latest > afterUtc ? latest : (DateTime?)null);
        }
    }
}
