using System.Text.Json;
using EduFocus.Ports.DTOs;
using EduFocus.UseCases.Cloud;
using EduFocus.UseCases.Tests.TestSupport;
using FluentAssertions;

namespace EduFocus.UseCases.Tests.Cloud;

public sealed class CloudOutboxServiceTests
{
    private static readonly DateTimeOffset Now = new(2026, 11, 17, 9, 0, 0, TimeSpan.Zero);

    [Fact]
    public async Task Enqueue_creates_a_pending_event_and_signals_the_worker()
    {
        var harness = new Harness();

        await harness.Service.EnqueueAsync(
            "daily_summary",
            JsonSerializer.SerializeToElement(new { sessionsCount = 3 }));

        var item = harness.Outbox.Items.Should().ContainSingle().Subject;
        item.ClientEventId.Should().NotBe(Guid.Empty);
        item.Type.Should().Be("daily_summary");
        item.PayloadJson.Should().Be("{\"sessionsCount\":3}");
        item.CreatedAtUtc.Should().Be(Now.UtcDateTime);
        item.IsPending.Should().BeTrue();
        harness.Notifier.SignalCount.Should().Be(1);
    }

    [Fact]
    public async Task Every_enqueue_gets_its_own_client_event_id()
    {
        var harness = new Harness();
        var payload = JsonSerializer.SerializeToElement(new { sessionsCount = 1 });

        await harness.Service.EnqueueAsync("daily_summary", payload);
        await harness.Service.EnqueueAsync("daily_summary", payload);

        harness.Outbox.Items.Select(item => item.ClientEventId).Should().OnlyHaveUniqueItems();
    }

    [Theory]
    [InlineData("unknown_type")]
    [InlineData("")]
    public async Task Unknown_event_types_are_rejected(string type)
    {
        var harness = new Harness();

        var act = async () => await harness.Service.EnqueueAsync(
            type,
            JsonSerializer.SerializeToElement(new { value = 1 }));

        await act.Should().ThrowAsync<ArgumentException>();
        harness.Outbox.Items.Should().BeEmpty();
        harness.Notifier.SignalCount.Should().Be(0);
    }

    [Fact]
    public async Task Non_object_payloads_are_rejected()
    {
        var harness = new Harness();

        var act = async () => await harness.Service.EnqueueAsync(
            "daily_summary",
            JsonSerializer.SerializeToElement(new[] { 1, 2 }));

        await act.Should().ThrowAsync<ArgumentException>();
        harness.Outbox.Items.Should().BeEmpty();
    }

    [Fact]
    public async Task Enqueue_trims_the_queue_and_logs_only_a_count()
    {
        var harness = new Harness();
        harness.Outbox.TrimRemoved = 4;

        await harness.Service.EnqueueAsync(
            "attention_alert",
            JsonSerializer.SerializeToElement(new { kind = "distracted" }));

        harness.Outbox.TrimCalls.Should().Be(1);
        harness.Logger.Warnings.Should().ContainSingle()
            .Which.Should().Contain("4 oldest event(s)")
            .And.NotContain("distracted");
    }

    [Fact]
    public async Task Status_is_read_from_the_repository()
    {
        var harness = new Harness();
        harness.Outbox.Status = new CloudOutboxStatus(2, 1, "network", Now.UtcDateTime.AddMinutes(-5));

        var status = await harness.Service.GetStatusAsync();

        status.PendingCount.Should().Be(2);
        status.FailedCount.Should().Be(1);
        status.LastError.Should().Be("network");
        status.LastSentAtUtc.Should().Be(Now.UtcDateTime.AddMinutes(-5));
    }

    private sealed class Harness
    {
        public Harness()
        {
            Service = new CloudOutboxService(
                Outbox,
                Notifier,
                Logger,
                new ManualTimeProvider(Now));
        }

        public FakeCloudOutboxRepository Outbox { get; } = new();

        public RecordingCloudOutboxNotifier Notifier { get; } = new();

        public RecordingLogger Logger { get; } = new();

        public CloudOutboxService Service { get; }
    }
}
