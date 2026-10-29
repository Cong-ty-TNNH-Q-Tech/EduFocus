using EduFocus.Domain.Enums;
using EduFocus.Ports.DTOs;
using EduFocus.UseCases.Lifecycle;
using EduFocus.UseCases.Tests.TestSupport;
using FluentAssertions;

namespace EduFocus.UseCases.Tests.Lifecycle;

public sealed class AppLifecycleServiceTests
{
    [Fact]
    public async Task Exit_with_wrong_pin_changes_nothing()
    {
        var harness = await LifecycleHarness.WithPinAsync();
        await harness.EnableProtectionAsync();

        var result = await harness.Lifecycle.ExitWithPinAsync("000000");

        result.CanExit.Should().BeFalse();
        result.AuthStatus.Should().Be(ParentAuthStatus.InvalidCredential);
        result.RemainingAttempts.Should().Be(4);
        result.LockoutUntilUtc.Should().BeNull();
        result.Error.Should().BeNull();

        harness.SettingsRow.ProtectionEnabled.Should().BeTrue();
        harness.SettingsRow.LastCleanShutdownAtUtc.Should().BeNull();
        harness.Sessions.Active.Should().NotBeNull();
        harness.Engine.RemoveCount.Should().Be(0);
        harness.Session.IsAuthenticated.Should().BeFalse();

        harness.Audits.Entries.Should().ContainSingle(x =>
            x.EventType == AuditEventType.AuthenticationFailed);
        harness.Audits.Entries.Should().NotContain(x =>
            x.EventType == AuditEventType.CleanExit);
    }

    [Fact]
    public async Task Exit_locks_out_after_repeated_wrong_pins()
    {
        var harness = await LifecycleHarness.WithPinAsync();

        AppExitResult result = null!;
        for (var attempt = 0; attempt < 5; attempt++)
            result = await harness.Lifecycle.ExitWithPinAsync("000000");

        result.CanExit.Should().BeFalse();
        result.AuthStatus.Should().Be(ParentAuthStatus.LockedOut);
        result.RemainingAttempts.Should().Be(0);
        result.LockoutUntilUtc.Should().Be(
            LifecycleHarness.Start.UtcDateTime.AddMinutes(15));
        harness.Session.IsAuthenticated.Should().BeFalse();
        harness.SettingsRow.LastCleanShutdownAtUtc.Should().BeNull();
    }

    [Fact]
    public async Task Exit_with_pin_cleans_state_and_records_a_clean_shutdown()
    {
        var harness = await LifecycleHarness.WithPinAsync();
        await harness.EnableProtectionAsync();

        var exitAt = LifecycleHarness.Start.UtcDateTime.AddMinutes(30);
        harness.Clock.Advance(TimeSpan.FromMinutes(30));

        var result = await harness.Lifecycle.ExitWithPinAsync(LifecycleHarness.Pin);

        result.CanExit.Should().BeTrue();
        result.AuthStatus.Should().Be(ParentAuthStatus.Success);
        result.Warnings.Should().BeEmpty();
        result.Error.Should().BeNull();

        harness.SettingsRow.ProtectionEnabled.Should().BeFalse();
        harness.SettingsRow.ProtectionSource.Should().Be(ProtectionSource.None);
        harness.SettingsRow.ScheduleOverrideUntilUtc.Should().BeNull();
        harness.SettingsRow.LastCleanShutdownAtUtc.Should().Be(exitAt);

        harness.Engine.RemoveCount.Should().Be(1);
        harness.Sessions.Active.Should().BeNull();

        var session = harness.Sessions.Sessions.Should().ContainSingle().Subject;
        session.Status.Should().Be(StudySessionStatus.Completed);
        session.EndedAtUtc.Should().Be(exitAt);

        harness.Session.IsAuthenticated.Should().BeFalse();

        harness.Audits.Entries.Select(x => x.EventType).Should().Equal(
            AuditEventType.PinSetup,
            AuditEventType.ProtectionOn,
            AuditEventType.ParentLogin,
            AuditEventType.ProtectionOff,
            AuditEventType.CleanExit);

        harness.Audits.Entries.Should().Contain(x =>
            x.EventType == AuditEventType.ProtectionOff && x.Actor == AuditActor.Parent);
        harness.Audits.Entries.Should().Contain(x =>
            x.EventType == AuditEventType.CleanExit && x.Actor == AuditActor.Parent);
    }

    [Fact]
    public async Task Exit_while_protection_is_off_still_requires_the_pin()
    {
        var harness = await LifecycleHarness.WithPinAsync();

        var rejected = await harness.Lifecycle.ExitWithPinAsync("000000");

        rejected.CanExit.Should().BeFalse();
        rejected.AuthStatus.Should().Be(ParentAuthStatus.InvalidCredential);
        harness.SettingsRow.LastCleanShutdownAtUtc.Should().BeNull();

        var result = await harness.Lifecycle.ExitWithPinAsync(LifecycleHarness.Pin);

        result.CanExit.Should().BeTrue();
        harness.Engine.RemoveCount.Should().Be(0);
        harness.Sessions.Sessions.Should().BeEmpty();
        harness.SettingsRow.ProtectionEnabled.Should().BeFalse();
        harness.SettingsRow.LastCleanShutdownAtUtc.Should().Be(
            LifecycleHarness.Start.UtcDateTime);
    }

    [Fact]
    public async Task Exit_clears_a_lingering_schedule_override()
    {
        var harness = await LifecycleHarness.WithPinAsync();
        harness.SettingsRow.DisableProtectionWithScheduleOverride(
            LifecycleHarness.Start.UtcDateTime.AddHours(1),
            LifecycleHarness.Start.UtcDateTime);

        var result = await harness.Lifecycle.ExitWithPinAsync(LifecycleHarness.Pin);

        result.CanExit.Should().BeTrue();
        harness.SettingsRow.ProtectionEnabled.Should().BeFalse();
        harness.SettingsRow.ScheduleOverrideUntilUtc.Should().BeNull();
        harness.SettingsRow.LastCleanShutdownAtUtc.Should().Be(
            LifecycleHarness.Start.UtcDateTime);
    }

    [Fact]
    public async Task Exit_without_removing_the_engine_keeps_the_app_running()
    {
        var harness = await LifecycleHarness.WithPinAsync();
        await harness.EnableProtectionAsync();
        harness.Engine.ThrowOnRemove = true;

        var result = await harness.Lifecycle.ExitWithPinAsync(LifecycleHarness.Pin);

        result.CanExit.Should().BeFalse();
        result.Error.Should().NotBeNullOrWhiteSpace();

        harness.SettingsRow.ProtectionEnabled.Should().BeTrue();
        harness.SettingsRow.LastCleanShutdownAtUtc.Should().BeNull();
        harness.Sessions.Active.Should().NotBeNull();
        harness.Sessions.Active!.Status.Should().Be(StudySessionStatus.Active);
        harness.Session.IsAuthenticated.Should().BeFalse();
        harness.Audits.Entries.Should().NotContain(x =>
            x.EventType == AuditEventType.CleanExit);
    }

    [Fact]
    public async Task Exit_still_succeeds_when_the_clean_marker_cannot_be_saved()
    {
        var harness = await LifecycleHarness.WithPinAsync();
        harness.Settings.ThrowOnUpdate = true;

        var result = await harness.Lifecycle.ExitWithPinAsync(LifecycleHarness.Pin);

        result.CanExit.Should().BeTrue();
        result.Warnings.Should().ContainSingle();
        harness.Logger.Errors.Should().Contain(message =>
            message.Contains("clean-shutdown marker", StringComparison.Ordinal));
        harness.Audits.Entries.Should().Contain(x =>
            x.EventType == AuditEventType.CleanExit);
    }

    [Fact]
    public async Task System_shutdown_interrupts_the_session_and_keeps_protection()
    {
        var harness = await LifecycleHarness.WithPinAsync();
        await harness.EnableProtectionAsync();
        harness.Clock.Advance(TimeSpan.FromMinutes(10));

        await harness.Lifecycle.NotifySystemShutdownAsync();

        var session = harness.Sessions.Sessions.Should().ContainSingle().Subject;
        session.Status.Should().Be(StudySessionStatus.Interrupted);
        session.Note.Should().Be(AppLifecycleService.SystemShutdownNote);
        session.EndedAtUtc.Should().Be(LifecycleHarness.Start.UtcDateTime);

        harness.SettingsRow.ProtectionEnabled.Should().BeTrue();
        harness.SettingsRow.ProtectionSource.Should().Be(ProtectionSource.Manual);
        harness.SettingsRow.LastCleanShutdownAtUtc.Should().BeNull();
        harness.Engine.RemoveCount.Should().Be(0);

        harness.Audits.Entries.Should().Contain(x =>
            x.EventType == AuditEventType.SessionInterrupted && x.Actor == AuditActor.System);
        harness.Audits.Entries.Should().Contain(x =>
            x.EventType == AuditEventType.SystemShutdown && x.Actor == AuditActor.System);
    }

    [Fact]
    public async Task Startup_after_system_shutdown_recovers_protection()
    {
        var harness = await LifecycleHarness.WithPinAsync();
        await harness.EnableProtectionAsync();
        harness.Clock.Advance(TimeSpan.FromMinutes(10));
        await harness.Lifecycle.NotifySystemShutdownAsync();

        var reconciled = await harness.Protection.ReconcileAsync();

        reconciled.Status.IsEnabled.Should().BeTrue();
        harness.Engine.LastReconcileActive.Should().BeTrue();
        harness.SettingsRow.ProtectionEnabled.Should().BeTrue();

        var recovered = harness.Sessions.Active;
        recovered.Should().NotBeNull();
        recovered!.Trigger.Should().Be(SessionTrigger.Recovery);
    }

    [Fact]
    public async Task System_shutdown_without_an_active_session_only_audits()
    {
        var harness = await LifecycleHarness.WithPinAsync();

        await harness.Lifecycle.NotifySystemShutdownAsync();

        harness.Sessions.Sessions.Should().BeEmpty();
        harness.SettingsRow.ProtectionEnabled.Should().BeFalse();
        harness.SettingsRow.LastCleanShutdownAtUtc.Should().BeNull();
        harness.Audits.Entries.Select(x => x.EventType).Should().Equal(
            AuditEventType.PinSetup,
            AuditEventType.SystemShutdown);
    }

    [Fact]
    public async Task System_shutdown_swallows_database_failures()
    {
        var harness = await LifecycleHarness.WithPinAsync();
        await harness.EnableProtectionAsync();
        harness.Sessions.ThrowOnUpdate = true;

        var act = () => harness.Lifecycle.NotifySystemShutdownAsync();

        await act.Should().NotThrowAsync();
        harness.Logger.Errors.Should().NotBeEmpty();
        harness.SettingsRow.ProtectionEnabled.Should().BeTrue();
        harness.SettingsRow.LastCleanShutdownAtUtc.Should().BeNull();
    }

    [Fact]
    public async Task System_shutdown_stops_the_heartbeat_before_interrupting_the_session()
    {
        var harness = await LifecycleHarness.WithPinAsync();
        await harness.EnableProtectionAsync();
        harness.Clock.Advance(TimeSpan.FromMinutes(10));

        await harness.Lifecycle.NotifySystemShutdownAsync();

        harness.Heartbeat.StopCount.Should().Be(1);
        harness.Heartbeat.IsRunning.Should().BeFalse();

        var session = harness.Sessions.Sessions.Should().ContainSingle().Subject;
        session.Status.Should().Be(StudySessionStatus.Interrupted);
        session.EndedAtUtc.Should().Be(LifecycleHarness.Start.UtcDateTime);
    }

    [Fact]
    public async Task Clean_exit_stops_the_heartbeat_via_protection_disable()
    {
        var harness = await LifecycleHarness.WithPinAsync();
        await harness.EnableProtectionAsync();
        harness.Clock.Advance(TimeSpan.FromMinutes(30));

        var result = await harness.Lifecycle.ExitWithPinAsync(LifecycleHarness.Pin);

        result.CanExit.Should().BeTrue();
        harness.Heartbeat.StopCount.Should().Be(1);
        harness.Heartbeat.IsRunning.Should().BeFalse();
        harness.Sessions.Sessions.Should().ContainSingle()
            .Which.Status.Should().Be(StudySessionStatus.Completed);
    }
}
