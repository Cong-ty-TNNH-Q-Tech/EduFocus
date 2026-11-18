using EduFocus.Domain.Entities;
using EduFocus.Domain.Enums;
using EduFocus.Ports.Outbound.Cloud;
using EduFocus.UseCases.Cloud;
using EduFocus.UseCases.Tests.TestSupport;
using FluentAssertions;

namespace EduFocus.UseCases.Tests.Cloud;

public sealed class CloudSecurityAlertProducerTests
{
    private static readonly DateTimeOffset Now = new(2026, 11, 17, 20, 0, 0, TimeSpan.Zero);
    private static readonly TimeZoneInfo UtcPlus7 = TimeZoneInfo.CreateCustomTimeZone(
        "EduFocus/Test-UTC7",
        TimeSpan.FromHours(7),
        "EduFocus Test UTC+7",
        "EduFocus Test UTC+7");

    [Fact]
    public async Task No_link_means_no_scan()
    {
        var harness = new Harness();
        harness.Audits.Entries.Add(Tamper(Now.UtcDateTime.AddHours(-1)));

        (await harness.Producer.TryScanAsync()).Should().Be(0);

        harness.Audits.ListCalls.Should().Be(0);
        harness.Sink.Enqueued.Should().BeEmpty();
    }

    [Fact]
    public async Task Pairing_that_is_not_complete_means_no_scan()
    {
        var harness = new Harness();
        var link = CloudLink.CreateDefault(Now.UtcDateTime);
        link.RecordConsent(Now.UtcDateTime);
        link.BeginPairing(
            CloudTestData.CloudBaseUrl,
            Guid.NewGuid(),
            "ABCD2345",
            CloudTestData.DesktopToken,
            Now.UtcDateTime.AddMinutes(10),
            Now.UtcDateTime);
        harness.Links.Stored = link;
        harness.Audits.Entries.Add(Tamper(Now.UtcDateTime.AddMinutes(-1)));

        (await harness.Producer.TryScanAsync()).Should().Be(0);

        harness.Audits.ListCalls.Should().Be(0);
        harness.Sink.Enqueued.Should().BeEmpty();
    }

    [Fact]
    public async Task Tamper_audits_queue_one_security_alert_each_with_a_deterministic_id()
    {
        var harness = new Harness();
        harness.Links.Stored = CloudTestData.Linked(Now.UtcDateTime.AddDays(-1));
        var first = Tamper(Now.UtcDateTime.AddMinutes(-50));
        var second = Tamper(Now.UtcDateTime.AddMinutes(-10));
        harness.Audits.Entries.Add(first);
        harness.Audits.Entries.Add(second);

        var queued = await harness.Producer.TryScanAsync();

        queued.Should().Be(2);
        harness.Audits.LastEventType.Should().Be(AuditEventType.TamperDetected);
        harness.Sink.Enqueued.Should().HaveCount(2);
        harness.Sink.Enqueued[0].Type.Should().Be(CloudEventTypes.SecurityAlert);
        harness.Sink.Enqueued[0].ClientEventId.Should().Be($"audit-{first.Id.Value:D}");
        harness.Sink.Enqueued[1].ClientEventId.Should().Be($"audit-{second.Id.Value:D}");
        harness.Sink.Enqueued[0].Payload.EnumerateObject().Select(property => property.Name)
            .Should().BeEquivalentTo(["kind", "localDate"]);
        harness.Sink.Enqueued[0].Payload.GetProperty("kind").GetString().Should().Be("clock_jump");
        harness.Sink.Enqueued[0].Payload.GetProperty("localDate").GetString().Should().Be("2026-11-18");
    }

    [Fact]
    public async Task Already_queued_audits_are_skipped()
    {
        var harness = new Harness();
        harness.Links.Stored = CloudTestData.Linked(Now.UtcDateTime.AddDays(-1));
        var first = Tamper(Now.UtcDateTime.AddMinutes(-50));
        var second = Tamper(Now.UtcDateTime.AddMinutes(-10));
        harness.Audits.Entries.Add(first);
        harness.Audits.Entries.Add(second);
        harness.Outbox.Items.Add(CloudOutboxEvent.Create(
            $"audit-{first.Id.Value:D}",
            CloudEventTypes.SecurityAlert,
            "{}",
            Now.UtcDateTime));

        var queued = await harness.Producer.TryScanAsync();

        queued.Should().Be(1);
        harness.Sink.Enqueued.Should().ContainSingle()
            .Which.ClientEventId.Should().Be($"audit-{second.Id.Value:D}");
    }

    [Fact]
    public async Task Audits_older_than_the_link_are_ignored()
    {
        var harness = new Harness();
        harness.Links.Stored = CloudTestData.Linked(Now.UtcDateTime);
        harness.Audits.Entries.Add(Tamper(Now.UtcDateTime.AddHours(-2)));

        (await harness.Producer.TryScanAsync()).Should().Be(0);

        harness.Audits.LastFromUtc.Should().Be(Now.UtcDateTime);
        harness.Sink.Enqueued.Should().BeEmpty();
    }

    [Fact]
    public async Task The_scan_window_is_six_days_when_the_link_is_older()
    {
        var harness = new Harness();
        harness.Links.Stored = CloudTestData.Linked(Now.UtcDateTime.AddDays(-30));
        harness.Audits.Entries.Add(Tamper(Now.UtcDateTime.AddHours(-1)));

        (await harness.Producer.TryScanAsync()).Should().Be(1);

        harness.Audits.LastFromUtc.Should().Be(Now.UtcDateTime.AddDays(-6));
        harness.Audits.LastLimit.Should().Be(50);
    }

    [Fact]
    public async Task More_than_scan_limit_audits_are_all_enqueued_without_duplicates()
    {
        var harness = new Harness();
        harness.Links.Stored = CloudTestData.Linked(Now.UtcDateTime.AddDays(-1));
        const int total = 105;
        for (var i = 0; i < total; i++)
        {
            var entry = Tamper(Now.UtcDateTime.AddMinutes(-total + i));
            harness.Audits.Entries.Add(entry);
            if (i < 55)
            {
                harness.Outbox.Items.Add(CloudOutboxEvent.Create(
                    $"audit-{entry.Id.Value:D}",
                    CloudEventTypes.SecurityAlert,
                    "{}",
                    Now.UtcDateTime));
            }
        }

        var queued = await harness.Producer.TryScanAsync();

        queued.Should().Be(50);
        harness.Sink.Enqueued.Should().HaveCount(50);
        harness.Sink.Enqueued.Select(e => e.ClientEventId).Should().OnlyHaveUniqueItems();
    }

    [Fact]
    public async Task Other_audit_types_are_never_reported()
    {
        var harness = new Harness();
        harness.Links.Stored = CloudTestData.Linked(Now.UtcDateTime);
        harness.Audits.Entries.Add(AuditLog.Record(
            AuditActor.Parent,
            AuditEventType.ProtectionOff,
            "Parent turned protection off",
            Now.UtcDateTime.AddMinutes(-5)));

        (await harness.Producer.TryScanAsync()).Should().Be(0);

        harness.Sink.Enqueued.Should().BeEmpty();
    }

    [Fact]
    public async Task The_scan_stops_when_the_sink_rejects_an_event()
    {
        var harness = new Harness();
        harness.Links.Stored = CloudTestData.Linked(Now.UtcDateTime.AddDays(-1));
        harness.Audits.Entries.Add(Tamper(Now.UtcDateTime.AddMinutes(-50)));
        harness.Audits.Entries.Add(Tamper(Now.UtcDateTime.AddMinutes(-10)));
        harness.Sink.Result = false;

        (await harness.Producer.TryScanAsync()).Should().Be(0);

        harness.Sink.Attempts.Should().Be(1);
    }

    [Fact]
    public async Task Repository_failures_are_logged_and_swallowed()
    {
        var harness = new Harness();
        harness.Links.Stored = CloudTestData.Linked(Now.UtcDateTime);
        harness.Audits.ThrowOnList = true;

        (await harness.Producer.TryScanAsync()).Should().Be(0);

        harness.Logger.Warnings.Should().ContainSingle()
            .Which.Should().Contain(nameof(InvalidOperationException));
    }

    [Fact]
    public async Task Sink_failures_are_logged_and_swallowed()
    {
        var harness = new Harness();
        harness.Links.Stored = CloudTestData.Linked(Now.UtcDateTime.AddDays(-1));
        harness.Audits.Entries.Add(Tamper(Now.UtcDateTime.AddMinutes(-10)));
        harness.Sink.Throw = true;

        (await harness.Producer.TryScanAsync()).Should().Be(0);

        harness.Logger.Warnings.Should().ContainSingle()
            .Which.Should().Contain(nameof(InvalidOperationException));
    }

    private static AuditLog Tamper(DateTime occurredAtUtc) =>
        AuditLog.Record(
            AuditActor.System,
            AuditEventType.TamperDetected,
            "Clock jump detected",
            occurredAtUtc);

    private sealed class Harness
    {
        public Harness()
        {
            Producer = new CloudSecurityAlertProducer(
                Audits,
                Links,
                Outbox,
                Sink,
                Logger,
                new ManualTimeProvider(Now, UtcPlus7));
        }

        public RecordingAuditLogRepository Audits { get; } = new();

        public FakeCloudLinkRepository Links { get; } = new();

        public FakeCloudOutboxRepository Outbox { get; } = new();

        public RecordingCloudEventSink Sink { get; } = new();

        public RecordingLogger Logger { get; } = new();

        public CloudSecurityAlertProducer Producer { get; }
    }
}
