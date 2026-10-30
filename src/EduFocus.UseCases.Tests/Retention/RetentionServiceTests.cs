using EduFocus.Domain.Entities;
using EduFocus.Domain.Enums;
using EduFocus.Ports.DTOs;
using EduFocus.UseCases.Retention;
using EduFocus.UseCases.Tests.TestSupport;
using FluentAssertions;

namespace EduFocus.UseCases.Tests.Retention;

public sealed class RetentionServiceTests
{
    private static readonly DateTime Now = new(2026, 10, 30, 3, 0, 0, DateTimeKind.Utc);

    [Fact]
    public async Task Run_uses_each_configured_retention_window()
    {
        var harness = new RetentionHarness();
        harness.Settings.Stored!.UpdateRetention(activityDays: 14, eventDays: 60, sessionDays: 200, Now);

        await harness.Service.RunOnceAsync();

        harness.Repository.ActivityCutoff.Should().Be(Now.AddDays(-14));
        harness.Repository.EventCutoff.Should().Be(Now.AddDays(-60));
        harness.Repository.SessionCutoff.Should().Be(Now.AddDays(-200));
        harness.Repository.AuditCutoff.Should().Be(Now.AddDays(-365));
        harness.Repository.BatchSize.Should().Be(RetentionOptions.DefaultBatchSize);
    }

    [Fact]
    public async Task Run_deletes_batches_until_the_last_batch_is_short()
    {
        var harness = new RetentionHarness();
        harness.Repository.ActivityBatches.Enqueue(500);
        harness.Repository.ActivityBatches.Enqueue(500);
        harness.Repository.ActivityBatches.Enqueue(120);

        var result = await harness.Service.RunOnceAsync();

        harness.Repository.ActivityCalls.Should().Be(3);
        result.Deleted.ActivitySamples.Should().Be(1120);
        result.Completed.Should().BeTrue();
    }

    [Fact]
    public async Task Run_writes_an_audit_entry_with_counts_and_no_sensitive_data()
    {
        var harness = new RetentionHarness();
        harness.Correlation.Current = Guid.NewGuid();
        harness.Repository.ActivityBatches.Enqueue(10);
        harness.Repository.EventBatches.Enqueue(20);
        harness.Repository.AttemptBatches.Enqueue(30);
        harness.Repository.SessionBatches.Enqueue(40);
        harness.Repository.AuditBatches.Enqueue(50);

        await harness.Service.RunOnceAsync();

        var entry = harness.Audits.Entries.Should().ContainSingle().Subject;
        entry.Actor.Should().Be(AuditActor.System);
        entry.EventType.Should().Be(AuditEventType.RetentionApplied);
        entry.OccurredAtUtc.Should().Be(Now);
        entry.CorrelationId.Should().Be(harness.Correlation.Current);
        entry.Description.Should().Contain("10 activity sample(s)")
            .And.Contain("20 attention event(s)")
            .And.Contain("30 blocked attempt(s)")
            .And.Contain("40 session(s)")
            .And.Contain("50 audit row(s)")
            .And.NotContain("Incomplete");
    }

    [Fact]
    public async Task Failing_table_is_reported_and_the_other_tables_still_run()
    {
        var harness = new RetentionHarness();
        harness.Repository.ActivityBatches.Enqueue(500);
        harness.Repository.FailActivityCall.Add(2);
        harness.Repository.EventBatches.Enqueue(7);
        harness.Repository.SessionBatches.Enqueue(3);

        var result = await harness.Service.RunOnceAsync();

        result.Completed.Should().BeFalse();
        result.Failures.Should().Equal("ActivitySamples");
        result.Deleted.ActivitySamples.Should().Be(500);
        result.Deleted.AttentionEvents.Should().Be(7);
        result.Deleted.Sessions.Should().Be(3);
        harness.Repository.EventCalls.Should().Be(1);
        harness.Repository.AttemptCalls.Should().Be(1);
        harness.Repository.VacuumCalls.Should().Be(1);

        var entry = harness.Audits.Entries.Should().ContainSingle().Subject;
        entry.Description.Should().Contain("Incomplete: ActivitySamples");
        harness.Logger.Errors.Should().ContainSingle()
            .Which.Should().Contain("ActivitySamples");
    }

    [Fact]
    public async Task Missing_settings_skip_every_delete_and_record_nothing()
    {
        var harness = new RetentionHarness(withSettings: false);

        var result = await harness.Service.RunOnceAsync();

        result.Completed.Should().BeFalse();
        result.Deleted.Should().Be(RetentionCounts.Empty);
        result.Failures.Should().Equal("Settings");
        harness.Repository.ActivityCalls.Should().Be(0);
        harness.Repository.EventCalls.Should().Be(0);
        harness.Repository.AttemptCalls.Should().Be(0);
        harness.Repository.SessionCalls.Should().Be(0);
        harness.Repository.AuditCalls.Should().Be(0);
        harness.Repository.VacuumCalls.Should().Be(0);
        harness.Audits.Entries.Should().BeEmpty();
        harness.Logger.Warnings.Should().ContainSingle()
            .Which.Should().Contain("settings are missing");
    }

    [Fact]
    public async Task Settings_read_failure_skips_every_delete()
    {
        var harness = new RetentionHarness();
        harness.Settings.ThrowOnRead = true;

        var result = await harness.Service.RunOnceAsync();

        result.Completed.Should().BeFalse();
        result.Failures.Should().Equal("Settings");
        harness.Repository.ActivityCalls.Should().Be(0);
        harness.Repository.VacuumCalls.Should().Be(0);
        harness.Audits.Entries.Should().BeEmpty();
        harness.Logger.Errors.Should().ContainSingle();
    }

    [Fact]
    public async Task Vacuum_failure_marks_the_run_incomplete_but_keeps_counts()
    {
        var harness = new RetentionHarness();
        harness.Repository.ActivityBatches.Enqueue(12);
        harness.Repository.ThrowOnVacuum = true;

        var result = await harness.Service.RunOnceAsync();

        result.Completed.Should().BeFalse();
        result.Failures.Should().Equal("Vacuum");
        result.Deleted.ActivitySamples.Should().Be(12);
        harness.Audits.Entries.Should().ContainSingle()
            .Which.Description.Should().Contain("Incomplete: Vacuum");
        harness.Logger.Warnings.Should().ContainSingle();
    }

    [Fact]
    public async Task Audit_write_failure_is_reported_without_losing_the_result()
    {
        var harness = new RetentionHarness();
        harness.Audits.ThrowOnAdd = true;
        harness.Repository.ActivityBatches.Enqueue(5);

        var result = await harness.Service.RunOnceAsync();

        result.Completed.Should().BeFalse();
        result.Failures.Should().Equal("AuditLog");
        result.Deleted.ActivitySamples.Should().Be(5);
        harness.Logger.Errors.Should().ContainSingle()
            .Which.Should().Contain("audit");
    }

    [Fact]
    public async Task An_empty_run_still_vacuums_and_writes_an_audit_entry()
    {
        var harness = new RetentionHarness();

        var result = await harness.Service.RunOnceAsync();

        result.Completed.Should().BeTrue();
        result.Deleted.Should().Be(RetentionCounts.Empty);
        harness.Repository.VacuumCalls.Should().Be(1);
        harness.Audits.Entries.Should().ContainSingle()
            .Which.Description.Should().Contain("0 activity sample(s)");
    }

    [Fact]
    public async Task Concurrent_runs_are_serialized()
    {
        var harness = new RetentionHarness();
        harness.Repository.ActivityBatches.Enqueue(1);

        await Task.WhenAll(
            harness.Service.RunOnceAsync(),
            harness.Service.RunOnceAsync());

        harness.Repository.ActivityCalls.Should().Be(2);
        harness.Audits.Entries.Should().HaveCount(2);
    }

    [Fact]
    public void Options_reject_out_of_range_values()
    {
        Func<int, int, Action> create = (batch, auditDays) => () => new RetentionService(
            new FakeRetentionRepository(),
            new FakeAppSettingsRepository(),
            new RecordingAuditLogRepository(),
            new TestCorrelationContext(),
            new RecordingLogger(),
            new ManualTimeProvider(Now),
            new RetentionOptions { BatchSize = batch, AuditRetentionDays = auditDays });

        create(0, 365).Should().Throw<ArgumentOutOfRangeException>();
        create(10_001, 365).Should().Throw<ArgumentOutOfRangeException>();
        create(500, 0).Should().Throw<ArgumentOutOfRangeException>();
        create(500, 3651).Should().Throw<ArgumentOutOfRangeException>();
    }

    private sealed class RetentionHarness
    {
        public RetentionHarness(bool withSettings = true)
        {
            if (withSettings)
                Settings.Stored = AppSettings.CreateDefault(Now);

            Service = new RetentionService(
                Repository,
                Settings,
                Audits,
                Correlation,
                Logger,
                new ManualTimeProvider(Now),
                new RetentionOptions());
        }

        public FakeRetentionRepository Repository { get; } = new();

        public FakeAppSettingsRepository Settings { get; } = new();

        public RecordingAuditLogRepository Audits { get; } = new();

        public TestCorrelationContext Correlation { get; } = new();

        public RecordingLogger Logger { get; } = new();

        public RetentionService Service { get; }
    }
}
