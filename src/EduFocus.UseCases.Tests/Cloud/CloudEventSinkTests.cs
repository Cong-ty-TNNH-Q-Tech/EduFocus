using System.Text.Json;
using EduFocus.Domain.Entities;
using EduFocus.UseCases.Cloud;
using EduFocus.UseCases.Tests.TestSupport;
using FluentAssertions;

namespace EduFocus.UseCases.Tests.Cloud;

public sealed class CloudEventSinkTests
{
    private static readonly JsonElement Payload =
        JsonSerializer.SerializeToElement(new { sessionsCount = 2 });

    [Fact]
    public async Task No_op_when_the_link_is_absent()
    {
        var harness = new Harness();

        var queued = await harness.Sink.TryEnqueueAsync("daily_summary", Payload);

        queued.Should().BeFalse();
        harness.Outbox.Items.Should().BeEmpty();
        harness.Outbox.TrimCalls.Should().Be(0);
        harness.Logger.Warnings.Should().BeEmpty();
    }

    [Fact]
    public async Task No_op_when_the_link_is_disabled()
    {
        var harness = new Harness();
        harness.Links.Stored = CloudLink.CreateDefault(CloudTestData.Now);

        var queued = await harness.Sink.TryEnqueueAsync("daily_summary", Payload);

        queued.Should().BeFalse();
        harness.Outbox.Items.Should().BeEmpty();
    }

    [Fact]
    public async Task No_op_while_pairing_is_not_complete()
    {
        var harness = new Harness();
        var link = CloudLink.CreateDefault(CloudTestData.Now);
        link.RecordConsent(CloudTestData.Now);
        link.BeginPairing(
            CloudTestData.CloudBaseUrl,
            Guid.NewGuid(),
            "ABCD2345",
            CloudTestData.DesktopToken,
            CloudTestData.Now.AddMinutes(10),
            CloudTestData.Now);
        harness.Links.Stored = link;

        var queued = await harness.Sink.TryEnqueueAsync("daily_summary", Payload);

        queued.Should().BeFalse();
        harness.Outbox.Items.Should().BeEmpty();
    }

    [Fact]
    public async Task Linked_sink_enqueues_the_event_with_a_fresh_id()
    {
        var harness = new Harness();
        harness.Links.Stored = CloudTestData.Linked();

        var queued = await harness.Sink.TryEnqueueAsync("daily_summary", Payload);

        queued.Should().BeTrue();
        var item = harness.Outbox.Items.Should().ContainSingle().Subject;
        item.Type.Should().Be("daily_summary");
        item.PayloadJson.Should().Be(Payload.GetRawText());
        Guid.TryParse(item.ClientEventId, out _).Should().BeTrue();
    }

    [Fact]
    public async Task Storage_failures_are_logged_and_swallowed()
    {
        var harness = new Harness();
        harness.Links.Stored = CloudTestData.Linked();
        harness.Links.ThrowOnGet = true;

        var queued = await harness.Sink.TryEnqueueAsync("daily_summary", Payload);

        queued.Should().BeFalse();
        harness.Logger.Warnings.Should().ContainSingle()
            .Which.Should().Contain(nameof(InvalidOperationException));
    }

    private sealed class Harness
    {
        public Harness()
        {
            Outbox = new FakeCloudOutboxRepository();
            Sink = new CloudEventSink(
                Links,
                new CloudOutboxService(
                    Outbox,
                    new RecordingCloudOutboxNotifier(),
                    Logger,
                    new ManualTimeProvider(CloudTestData.Now)),
                Logger);
        }

        public FakeCloudLinkRepository Links { get; } = new();

        public FakeCloudOutboxRepository Outbox { get; }

        public RecordingLogger Logger { get; } = new();

        public CloudEventSink Sink { get; }
    }
}
