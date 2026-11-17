using EduFocus.Ports.DTOs;
using EduFocus.Ports.Inbound;
using EduFocus.Ports.Outbound.Cloud;
using EduFocus.UseCases.Cloud;
using EduFocus.UseCases.Tests.TestSupport;
using FluentAssertions;

namespace EduFocus.UseCases.Tests.Cloud;

public sealed class CloudEventProtectionDecoratorTests
{
    private static readonly DateTimeOffset Now = new(2026, 11, 17, 20, 30, 0, TimeSpan.Zero);
    private static readonly TimeZoneInfo UtcPlus7 = TimeZoneInfo.CreateCustomTimeZone(
        "EduFocus/Test-UTC7",
        TimeSpan.FromHours(7),
        "EduFocus Test UTC+7",
        "EduFocus Test UTC+7");

    [Fact]
    public async Task Parent_disable_reports_protection_off_and_captures_the_summary()
    {
        var harness = new Harness();

        var result = await harness.Decorator.DisableAsync();

        result.Changed.Should().BeTrue();
        harness.Protection.DisableCalls.Should().Be(1);
        harness.Sessions.DailySummaryRequests.Should().ContainSingle();

        var queued = harness.Sink.Enqueued.Should().ContainSingle().Subject;
        queued.Type.Should().Be(CloudEventTypes.ProtectionOff);
        queued.ClientEventId.Should().BeNull();
        queued.Payload.EnumerateObject().Select(property => property.Name)
            .Should().BeEquivalentTo(["source", "localDate"]);
        queued.Payload.GetProperty("source").GetString().Should().Be("parent");
        queued.Payload.GetProperty("localDate").GetString().Should().Be("2026-11-18");
    }

    [Fact]
    public async Task Exit_disable_reports_the_exit_source()
    {
        var harness = new Harness();

        await harness.Decorator.DisableForExitAsync();

        harness.Protection.ExitCalls.Should().Be(1);
        harness.Sink.Enqueued.Should().ContainSingle()
            .Which.Payload.GetProperty("source").GetString().Should().Be("exit");
    }

    [Fact]
    public async Task Schedule_disable_captures_the_summary_without_an_alert()
    {
        var harness = new Harness();

        await harness.Decorator.DisableForScheduleAsync();

        harness.Protection.ScheduleCalls.Should().Be(1);
        harness.Sink.Enqueued.Should().BeEmpty();
        harness.Sessions.DailySummaryRequests.Should().ContainSingle();
    }

    [Fact]
    public async Task No_change_queues_nothing()
    {
        var harness = new Harness();
        harness.Protection.Changed = false;

        var result = await harness.Decorator.DisableAsync();

        result.Changed.Should().BeFalse();
        harness.Sink.Enqueued.Should().BeEmpty();
        harness.Sessions.DailySummaryRequests.Should().BeEmpty();
    }

    [Fact]
    public async Task Protection_already_off_skips_the_alert_but_still_captures_the_session()
    {
        var harness = new Harness();
        harness.Protection.WasEnabled = false;

        await harness.Decorator.DisableAsync();

        harness.Sink.Enqueued.Should().BeEmpty();
        harness.Sessions.DailySummaryRequests.Should().ContainSingle();
    }

    [Fact]
    public async Task An_unknown_previous_state_skips_the_alert()
    {
        var harness = new Harness();
        harness.Protection.ThrowOnStatus = true;

        await harness.Decorator.DisableAsync();

        harness.Sink.Enqueued.Should().BeEmpty();
        harness.Sessions.DailySummaryRequests.Should().ContainSingle();
        harness.Logger.Warnings.Should().ContainSingle()
            .Which.Should().Contain(nameof(InvalidOperationException));
    }

    [Fact]
    public async Task Sink_failures_do_not_change_the_protection_result()
    {
        var harness = new Harness();
        harness.Sink.Throw = true;

        var result = await harness.Decorator.DisableAsync();

        result.Changed.Should().BeTrue();
        harness.Sessions.DailySummaryRequests.Should().ContainSingle();
        harness.Logger.Warnings.Should().ContainSingle()
            .Which.Should().Contain(nameof(InvalidOperationException));
    }

    [Fact]
    public async Task Enable_reconcile_and_schedule_ops_pass_through_silently()
    {
        var harness = new Harness();

        await harness.Decorator.EnableAsync();
        await harness.Decorator.EnableForScheduleAsync();
        await harness.Decorator.ReconcileAsync();
        await harness.Decorator.ReloadRulesAsync();

        harness.Sink.Enqueued.Should().BeEmpty();
        harness.Sessions.DailySummaryRequests.Should().BeEmpty();
        harness.Protection.EnableCalls.Should().Be(2);
        harness.Protection.ReconcileCalls.Should().Be(1);
        harness.Protection.ReloadCalls.Should().Be(1);
    }

    private sealed class Harness
    {
        public Harness()
        {
            var clock = new ManualTimeProvider(Now, UtcPlus7);
            var summaries = new CloudDailySummaryProducer(Sessions, Sink, Logger, clock);
            Decorator = new CloudEventProtectionDecorator(Protection, summaries, Sink, Logger, clock);
        }

        public FakeProtection Protection { get; } = new();

        public FakeStudySessionRepository Sessions { get; } = new();

        public RecordingCloudEventSink Sink { get; } = new();

        public RecordingLogger Logger { get; } = new();

        public CloudEventProtectionDecorator Decorator { get; }
    }

    private sealed class FakeProtection : IProtectionUseCase
    {
        public bool WasEnabled { get; set; } = true;

        public bool Changed { get; set; } = true;

        public bool ThrowOnStatus { get; set; }

        public int DisableCalls { get; private set; }

        public int ExitCalls { get; private set; }

        public int ScheduleCalls { get; private set; }

        public int EnableCalls { get; private set; }

        public int ReconcileCalls { get; private set; }

        public int ReloadCalls { get; private set; }

        public Task<ProtectionStatus> GetStatusAsync(CancellationToken cancellationToken = default) =>
            ThrowOnStatus
                ? throw new InvalidOperationException("The protection service is unavailable.")
                : Task.FromResult(new ProtectionStatus(WasEnabled, null, true));

        public Task<ProtectionOperationResult> EnableAsync(CancellationToken cancellationToken = default)
        {
            EnableCalls++;
            return Result();
        }

        public Task<ProtectionOperationResult> DisableAsync(CancellationToken cancellationToken = default)
        {
            DisableCalls++;
            return Result();
        }

        public Task<ProtectionOperationResult> DisableForExitAsync(CancellationToken cancellationToken = default)
        {
            ExitCalls++;
            return Result();
        }

        public Task<ProtectionOperationResult> ReconcileAsync(CancellationToken cancellationToken = default)
        {
            ReconcileCalls++;
            return Result();
        }

        public Task<ProtectionOperationResult> EnableForScheduleAsync(CancellationToken cancellationToken = default)
        {
            EnableCalls++;
            return Result();
        }

        public Task<ProtectionOperationResult> DisableForScheduleAsync(CancellationToken cancellationToken = default)
        {
            ScheduleCalls++;
            return Result();
        }

        public Task<ProtectionOperationResult> ReloadRulesAsync(CancellationToken cancellationToken = default)
        {
            ReloadCalls++;
            return Result();
        }

        private Task<ProtectionOperationResult> Result() =>
            Task.FromResult(new ProtectionOperationResult(
                new ProtectionStatus(false, null, true),
                Changed,
                []));
    }
}
