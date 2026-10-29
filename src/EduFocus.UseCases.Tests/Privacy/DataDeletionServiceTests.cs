using EduFocus.Domain.Enums;
using EduFocus.Ports.DTOs;
using EduFocus.Ports.Security;
using EduFocus.UseCases.Privacy;
using EduFocus.UseCases.Tests.TestSupport;
using FluentAssertions;

namespace EduFocus.UseCases.Tests.Privacy;

public sealed class DataDeletionServiceTests
{
    private const string WrongPin = "111111";

    [Fact]
    public async Task Delete_requires_an_authenticated_parent_session()
    {
        var harness = await PrivacyHarness.CreateAsync(authenticated: false);
        Seed(harness);

        var act = () => harness.Deletion.DeleteAllAsync(Request());

        await act.Should().ThrowAsync<ParentAuthorizationException>();
        harness.Data.DeletedTables.Should().BeEmpty();
        harness.Audits.Entries.Should().BeEmpty();
    }

    [Fact]
    public async Task Wrong_confirmation_fails_before_the_pin_is_checked()
    {
        var harness = await PrivacyHarness.CreateAsync();
        Seed(harness);

        var result = await harness.Deletion.DeleteAllAsync(
            new DataDeletionRequest(WrongPin, "xoa"));

        result.Status.Should().Be(DataDeletionStatus.ConfirmationMismatch);
        harness.Data.DeletedTables.Should().BeEmpty();
        harness.Audits.Entries.Should().BeEmpty();
    }

    [Fact]
    public async Task Wrong_pin_returns_remaining_attempts_and_deletes_nothing()
    {
        var harness = await PrivacyHarness.CreateAsync();
        Seed(harness);

        var result = await harness.Deletion.DeleteAllAsync(
            new DataDeletionRequest(WrongPin, DataDeletionService.RequiredConfirmation));

        result.Status.Should().Be(DataDeletionStatus.InvalidPin);
        result.RemainingAttempts.Should().Be(4);
        harness.Data.DeletedTables.Should().BeEmpty();
        harness.Audits.Entries.Should().BeEmpty();
    }

    [Fact]
    public async Task Repeated_wrong_pins_lock_out_and_never_delete()
    {
        var harness = await PrivacyHarness.CreateAsync();
        Seed(harness);

        DataDeletionResult? last = null;
        for (var attempt = 0; attempt < 5; attempt++)
        {
            last = await harness.Deletion.DeleteAllAsync(
                new DataDeletionRequest(WrongPin, DataDeletionService.RequiredConfirmation));
        }

        last!.Status.Should().Be(DataDeletionStatus.LockedOut);
        last.LockoutUntilUtc.Should().NotBeNull();
        harness.Data.DeletedTables.Should().BeEmpty();
        harness.Audits.Entries.Should().BeEmpty();
    }

    [Fact]
    public async Task Missing_credentials_report_not_configured()
    {
        var harness = await PrivacyHarness.CreateAsync(configurePin: false);
        Seed(harness);

        var result = await harness.Deletion.DeleteAllAsync(Request());

        result.Status.Should().Be(DataDeletionStatus.NotConfigured);
        harness.Data.DeletedTables.Should().BeEmpty();
        harness.Audits.Entries.Should().BeEmpty();
    }

    [Fact]
    public async Task Success_deletes_finished_data_in_batches_and_audits_counts()
    {
        var harness = await PrivacyHarness.CreateAsync(batchSize: 2);
        Seed(harness);

        var result = await harness.Deletion.DeleteAllAsync(Request());

        result.Status.Should().Be(DataDeletionStatus.Success);
        result.Failures.Should().BeEmpty();
        result.Deleted.Should().Be(new DataDeletionCounts(2, 5, 4, 2));

        harness.Data.Sessions.Should().Be(1);
        harness.Data.ActiveSessions.Should().Be(1);
        harness.Data.ActivitySamples.Should().Be(0);
        harness.Data.AttentionEvents.Should().Be(0);
        harness.Data.BlockedAttempts.Should().Be(0);
        harness.Data.VacuumCalls.Should().Be(1);
        harness.Data.DeletedTables.Should().Contain("StudySessions:2");

        var audit = harness.Audits.Entries.Should()
            .ContainSingle(x => x.EventType == AuditEventType.DataDeleted)
            .Subject;
        audit.Actor.Should().Be(AuditActor.Parent);
        audit.Description.Should().Contain("2 session(s)");
        audit.Description.Should().NotContain(PrivacyHarness.CorrectPin);
    }

    [Fact]
    public async Task Active_session_rows_are_never_deleted()
    {
        var harness = await PrivacyHarness.CreateAsync();
        harness.Data.Sessions = 1;
        harness.Data.ActiveSessions = 1;

        var result = await harness.Deletion.DeleteAllAsync(Request());

        result.Status.Should().Be(DataDeletionStatus.Success);
        result.Deleted.Sessions.Should().Be(0);
        harness.Data.Sessions.Should().Be(1);
        harness.Data.DeletedTables.Should().NotContain(x => x.StartsWith("StudySessions:", StringComparison.Ordinal));
    }

    [Fact]
    public async Task Table_failure_is_reported_as_incomplete_and_audited()
    {
        var harness = await PrivacyHarness.CreateAsync();
        Seed(harness);
        harness.Data.FailingTables.Add("AttentionEvents");

        var result = await harness.Deletion.DeleteAllAsync(Request());

        result.Status.Should().Be(DataDeletionStatus.Incomplete);
        result.Failures.Should().Equal("AttentionEvents");
        harness.Data.AttentionEvents.Should().Be(4);

        var audit = harness.Audits.Entries.Should()
            .ContainSingle(x => x.EventType == AuditEventType.DataDeleted)
            .Subject;
        audit.Description.Should().Contain("Incomplete: AttentionEvents");
    }

    [Fact]
    public async Task Audit_failure_turns_a_successful_delete_into_incomplete()
    {
        var harness = await PrivacyHarness.CreateAsync();
        Seed(harness);
        harness.Audits.ThrowOnAdd = true;

        var result = await harness.Deletion.DeleteAllAsync(Request());

        result.Status.Should().Be(DataDeletionStatus.Incomplete);
        result.Failures.Should().Equal("AuditLog");
        harness.Data.ActivitySamples.Should().Be(0);
        harness.Logger.Errors.Should().Contain(x => x.Contains("audit", StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public async Task Vacuum_failure_is_non_fatal()
    {
        var harness = await PrivacyHarness.CreateAsync();
        Seed(harness);
        harness.Data.ThrowOnVacuum = true;

        var result = await harness.Deletion.DeleteAllAsync(Request());

        result.Status.Should().Be(DataDeletionStatus.Success);
        harness.Data.VacuumCalls.Should().Be(1);
        harness.Logger.Warnings.Should().Contain(x => x.Contains("vacuum", StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public async Task Cancellation_mid_way_propagates_and_writes_no_success_audit()
    {
        var harness = await PrivacyHarness.CreateAsync(batchSize: 2);
        Seed(harness);
        using var cancellation = new CancellationTokenSource();
        harness.Data.AfterDelete = table =>
        {
            if (table == "ActivitySamples")
                cancellation.Cancel();
        };

        var act = () => harness.Deletion.DeleteAllAsync(Request(), cancellation.Token);

        await act.Should().ThrowAsync<OperationCanceledException>();
        harness.Data.ActivitySamples.Should().Be(3);
        harness.Data.AttentionEvents.Should().Be(4);
        harness.Data.VacuumCalls.Should().Be(0);
        harness.Audits.Entries.Should().NotContain(x => x.EventType == AuditEventType.DataDeleted);
    }

    private static DataDeletionRequest Request() =>
        new(PrivacyHarness.CorrectPin, DataDeletionService.RequiredConfirmation);

    private static void Seed(PrivacyHarness harness)
    {
        harness.Data.Sessions = 3;
        harness.Data.ActiveSessions = 1;
        harness.Data.ActivitySamples = 5;
        harness.Data.AttentionEvents = 4;
        harness.Data.BlockedAttempts = 2;
    }
}
