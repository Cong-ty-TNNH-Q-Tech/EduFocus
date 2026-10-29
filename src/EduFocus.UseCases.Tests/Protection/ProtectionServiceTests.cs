using EduFocus.Domain.Entities;
using EduFocus.Domain.Enums;
using EduFocus.Ports.Protection;
using EduFocus.Ports.Security;
using EduFocus.UseCases.Tests.TestSupport;
using FluentAssertions;

namespace EduFocus.UseCases.Tests.Protection;

public sealed class ProtectionServiceTests
{
    [Fact]
    public async Task Enabling_requires_an_authenticated_parent()
    {
        var harness = new ProtectionHarness();

        var act = () => harness.Service.EnableAsync();

        await act.Should().ThrowAsync<ParentAuthorizationException>();
        harness.Engine.ApplyCount.Should().Be(0);
        harness.Sessions.Sessions.Should().BeEmpty();
        harness.SettingsRow.ProtectionEnabled.Should().BeFalse();
    }

    [Fact]
    public async Task Enabling_applies_engine_starts_session_and_audits()
    {
        var harness = new ProtectionHarness();
        harness.Session.Authenticate();
        harness.SettingsRow.SetScheduleOverrideUntil(
            ProtectionHarness.Start.UtcDateTime.AddHours(1),
            ProtectionHarness.Start.UtcDateTime);

        var result = await harness.Service.EnableAsync();

        result.Changed.Should().BeTrue();
        result.Status.IsEnabled.Should().BeTrue();
        result.Status.ActiveSessionStartedAtUtc.Should().Be(ProtectionHarness.Start.UtcDateTime);
        result.Warnings.Should().BeEmpty();

        harness.SettingsRow.ProtectionEnabled.Should().BeTrue();
        harness.SettingsRow.ScheduleOverrideUntilUtc.Should().BeNull();
        harness.Engine.ApplyCount.Should().Be(1);

        var session = harness.Sessions.Active;
        session.Should().NotBeNull();
        session!.Trigger.Should().Be(SessionTrigger.Manual);
        session.MonitoringEnabled.Should().BeTrue();

        harness.Audits.Entries.Should().ContainSingle(x =>
            x.EventType == AuditEventType.ProtectionOn && x.Actor == AuditActor.Parent);
    }

    [Fact]
    public async Task Enabling_twice_is_idempotent()
    {
        var harness = new ProtectionHarness();
        harness.Session.Authenticate();

        var first = await harness.Service.EnableAsync();
        var sessionId = harness.Sessions.Active!.Id;
        var second = await harness.Service.EnableAsync();

        first.Changed.Should().BeTrue();
        second.Changed.Should().BeFalse();
        harness.Sessions.Sessions.Should().ContainSingle();
        harness.Sessions.Active!.Id.Should().Be(sessionId);
        harness.Settings.UpdateCount.Should().Be(1);
        harness.Audits.Entries.Count(x => x.EventType == AuditEventType.ProtectionOn).Should().Be(1);
    }

    [Fact]
    public async Task Disabling_requires_an_authenticated_parent()
    {
        var harness = new ProtectionHarness();
        harness.Session.Authenticate();
        await harness.Service.EnableAsync();
        harness.Session.Lock();

        var act = () => harness.Service.DisableAsync();

        await act.Should().ThrowAsync<ParentAuthorizationException>();
        harness.SettingsRow.ProtectionEnabled.Should().BeTrue();
        harness.Sessions.Active.Should().NotBeNull();
        harness.Engine.RemoveCount.Should().Be(0);
    }

    [Fact]
    public async Task Disabling_removes_engine_completes_session_and_audits()
    {
        var harness = new ProtectionHarness();
        harness.Session.Authenticate();
        await harness.Service.EnableAsync();
        harness.Clock.Advance(TimeSpan.FromMinutes(30));
        harness.Session.Authenticate();

        var result = await harness.Service.DisableAsync();

        result.Changed.Should().BeTrue();
        result.Status.IsEnabled.Should().BeFalse();
        result.Status.ActiveSessionStartedAtUtc.Should().BeNull();

        harness.SettingsRow.ProtectionEnabled.Should().BeFalse();
        harness.Engine.RemoveCount.Should().Be(1);
        harness.Sessions.Active.Should().BeNull();

        harness.Sessions.Sessions.Should().ContainSingle();
        var session = harness.Sessions.Sessions[0];
        session.Status.Should().Be(StudySessionStatus.Completed);
        session.EndedAtUtc.Should().Be(ProtectionHarness.Start.UtcDateTime.AddMinutes(30));

        harness.Audits.Entries.Select(x => x.EventType).Should().Equal(
            AuditEventType.ProtectionOn,
            AuditEventType.ProtectionOff);
    }

    [Fact]
    public async Task Disabling_twice_is_idempotent()
    {
        var harness = new ProtectionHarness();
        harness.Session.Authenticate();
        await harness.Service.EnableAsync();
        await harness.Service.DisableAsync();

        var second = await harness.Service.DisableAsync();

        second.Changed.Should().BeFalse();
        harness.Engine.RemoveCount.Should().Be(1);
        harness.Settings.UpdateCount.Should().Be(2);
    }

    [Fact]
    public async Task Engine_apply_failure_keeps_protection_off()
    {
        var harness = new ProtectionHarness();
        harness.Session.Authenticate();
        harness.Engine.ThrowOnApply = true;

        var act = () => harness.Service.EnableAsync();

        await act.Should().ThrowAsync<ProtectionEngineException>();
        harness.SettingsRow.ProtectionEnabled.Should().BeFalse();
        harness.Sessions.Sessions.Should().BeEmpty();
        harness.Audits.Entries.Should().BeEmpty();
    }

    [Fact]
    public async Task Session_save_failure_rolls_back_the_engine()
    {
        var harness = new ProtectionHarness();
        harness.Session.Authenticate();
        harness.Sessions.ThrowOnAdd = true;

        var act = () => harness.Service.EnableAsync();

        await act.Should().ThrowAsync<InvalidOperationException>();
        harness.Engine.RemoveCount.Should().Be(1);
        harness.SettingsRow.ProtectionEnabled.Should().BeFalse();
        harness.Audits.Entries.Should().BeEmpty();
    }

    [Fact]
    public async Task Engine_remove_failure_keeps_protection_on()
    {
        var harness = new ProtectionHarness();
        harness.Session.Authenticate();
        await harness.Service.EnableAsync();
        harness.Engine.ThrowOnRemove = true;

        var act = () => harness.Service.DisableAsync();

        await act.Should().ThrowAsync<ProtectionEngineException>();
        harness.SettingsRow.ProtectionEnabled.Should().BeTrue();
        harness.Sessions.Active.Should().NotBeNull();
        harness.Audits.Entries.Select(x => x.EventType).Should().Equal(AuditEventType.ProtectionOn);
    }

    [Fact]
    public async Task Reconcile_with_protection_on_interrupts_stale_session_and_recovers()
    {
        var harness = new ProtectionHarness();
        harness.SettingsRow.EnableProtection(ProtectionHarness.Start.UtcDateTime);
        var stale = StudySession.Start(
            ProtectionHarness.Start.UtcDateTime,
            new DateOnly(2026, 10, 29),
            SessionTrigger.Manual,
            monitoringEnabled: true);
        harness.Sessions.Sessions.Add(stale);
        harness.Engine.Warnings = ["Block page port is busy."];
        harness.Clock.Advance(TimeSpan.FromMinutes(10));

        var result = await harness.Service.ReconcileAsync();

        stale.Status.Should().Be(StudySessionStatus.Interrupted);
        stale.EndedAtUtc.Should().Be(ProtectionHarness.Start.UtcDateTime);

        result.Changed.Should().BeTrue();
        result.Status.IsEnabled.Should().BeTrue();
        result.Status.ActiveSessionStartedAtUtc.Should()
            .Be(ProtectionHarness.Start.UtcDateTime.AddMinutes(10));
        result.Warnings.Should().ContainSingle().Which.Should().Be("Block page port is busy.");

        harness.Engine.LastReconcileActive.Should().BeTrue();
        harness.Sessions.Sessions.Should().HaveCount(2);
        var recovered = harness.Sessions.Active;
        recovered.Should().NotBeNull();
        recovered!.Trigger.Should().Be(SessionTrigger.Recovery);

        harness.Audits.Entries.Select(x => x.EventType).Should().Equal(
            AuditEventType.SessionInterrupted,
            AuditEventType.SessionRecovered);
    }

    [Fact]
    public async Task Reconcile_with_protection_off_cleans_resources_and_has_no_session()
    {
        var harness = new ProtectionHarness();

        var result = await harness.Service.ReconcileAsync();

        result.Changed.Should().BeFalse();
        result.Status.IsEnabled.Should().BeFalse();
        harness.Engine.ReconcileCount.Should().Be(1);
        harness.Engine.LastReconcileActive.Should().BeFalse();
        harness.Sessions.Sessions.Should().BeEmpty();
        harness.Audits.Entries.Should().BeEmpty();
    }

    [Fact]
    public async Task Reconcile_engine_failure_keeps_saved_state_and_warns()
    {
        var harness = new ProtectionHarness();
        harness.SettingsRow.EnableProtection(ProtectionHarness.Start.UtcDateTime);
        harness.Engine.ThrowOnReconcile = true;

        var result = await harness.Service.ReconcileAsync();

        result.Status.IsEnabled.Should().BeTrue();
        result.Warnings.Should().ContainSingle();
        harness.SettingsRow.ProtectionEnabled.Should().BeTrue();
        harness.Sessions.Active.Should().NotBeNull();
    }

    [Fact]
    public async Task GetStatus_reports_the_active_session()
    {
        var harness = new ProtectionHarness();
        harness.Session.Authenticate();
        await harness.Service.EnableAsync();

        var status = await harness.Service.GetStatusAsync();

        status.IsEnabled.Should().BeTrue();
        status.ActiveSessionStartedAtUtc.Should().Be(ProtectionHarness.Start.UtcDateTime);
        status.MonitoringEnabled.Should().BeTrue();
    }

    [Fact]
    public async Task Schedule_enable_requires_the_schedule_to_be_on()
    {
        var harness = new ProtectionHarness();

        var result = await harness.Service.EnableForScheduleAsync();

        result.Changed.Should().BeFalse();
        harness.SettingsRow.ProtectionEnabled.Should().BeFalse();
        harness.Engine.ApplyCount.Should().Be(0);
        harness.Sessions.Sessions.Should().BeEmpty();
    }

    [Fact]
    public async Task Schedule_enable_starts_a_schedule_session_as_the_system()
    {
        var harness = new ProtectionHarness();
        harness.SettingsRow.SetScheduleEnabled(true, ProtectionHarness.Start.UtcDateTime);

        var result = await harness.Service.EnableForScheduleAsync();

        result.Changed.Should().BeTrue();
        result.Status.IsEnabled.Should().BeTrue();
        harness.SettingsRow.ProtectionSource.Should().Be(ProtectionSource.Schedule);
        harness.Sessions.Active!.Trigger.Should().Be(SessionTrigger.Schedule);
        harness.Audits.Entries.Should().ContainSingle(x =>
            x.EventType == AuditEventType.ProtectionOn && x.Actor == AuditActor.System);
    }

    [Fact]
    public async Task Schedule_disable_leaves_manually_enabled_protection_on()
    {
        var harness = new ProtectionHarness();
        harness.Session.Authenticate();
        await harness.Service.EnableAsync();

        var result = await harness.Service.DisableForScheduleAsync();

        result.Changed.Should().BeFalse();
        harness.SettingsRow.ProtectionEnabled.Should().BeTrue();
        harness.SettingsRow.ProtectionSource.Should().Be(ProtectionSource.Manual);
        harness.Engine.RemoveCount.Should().Be(0);
        harness.Sessions.Active.Should().NotBeNull();
    }

    [Fact]
    public async Task Manual_disable_inside_an_active_window_sets_an_override()
    {
        var harness = new ProtectionHarness();
        harness.SettingsRow.SetScheduleEnabled(true, ProtectionHarness.Start.UtcDateTime);
        harness.ScheduleWindows.Windows.Add(
            ScheduleWindow.Create(ScheduleDays.Thursday, 480, 540, "Giờ học", ProtectionHarness.Start.UtcDateTime));
        harness.Session.Authenticate();
        await harness.Service.EnableAsync();

        var result = await harness.Service.DisableAsync();

        result.Status.IsEnabled.Should().BeFalse();
        harness.SettingsRow.ProtectionEnabled.Should().BeFalse();
        harness.SettingsRow.ScheduleOverrideUntilUtc.Should()
            .Be(ProtectionHarness.Start.UtcDateTime.AddHours(1));

        var description = harness.Audits.Entries
            .Single(x => x.EventType == AuditEventType.ProtectionOff)
            .Description;
        description.Should().Contain("override");
    }

    [Fact]
    public async Task Manual_disable_outside_a_window_does_not_create_an_override()
    {
        var harness = new ProtectionHarness();
        harness.SettingsRow.SetScheduleEnabled(true, ProtectionHarness.Start.UtcDateTime);
        harness.ScheduleWindows.Windows.Add(
            ScheduleWindow.Create(ScheduleDays.Thursday, 600, 660, "Muộn hơn", ProtectionHarness.Start.UtcDateTime));
        harness.Session.Authenticate();
        await harness.Service.EnableAsync();

        await harness.Service.DisableAsync();

        harness.SettingsRow.ScheduleOverrideUntilUtc.Should().BeNull();
    }
}
