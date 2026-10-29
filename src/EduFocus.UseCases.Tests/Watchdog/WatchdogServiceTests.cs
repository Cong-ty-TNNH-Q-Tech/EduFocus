using System.Text.Json;
using EduFocus.Domain.Entities;
using EduFocus.Domain.Enums;
using EduFocus.Ports.DTOs;
using EduFocus.UseCases.Tests.TestSupport;
using EduFocus.UseCases.Watchdog;
using FluentAssertions;

namespace EduFocus.UseCases.Tests.Watchdog;

public sealed class WatchdogServiceTests
{
    private static readonly DateTime Now = new(2026, 10, 29, 8, 0, 0, DateTimeKind.Utc);
    private const long AppPid = 1234;

    [Fact]
    public async Task Tick_stops_when_onboarding_is_not_complete()
    {
        var harness = new Harness();
        harness.Settings.Stored = Settings(protectionOn: false, onboarding: false);

        var result = await harness.Service.TickAsync(loginStart: true);

        result.Action.Should().Be(WatchdogTickAction.Stop);
        harness.Processes.StartCount.Should().Be(0);
    }

    [Fact]
    public async Task Tick_stops_when_settings_are_missing()
    {
        var harness = new Harness();

        var result = await harness.Service.TickAsync(loginStart: true);

        result.Action.Should().Be(WatchdogTickAction.Stop);
        harness.Processes.StartCount.Should().Be(0);
    }

    [Fact]
    public async Task Tick_starts_the_app_at_login_when_protection_is_off_and_start_with_windows_is_on()
    {
        var harness = new Harness();
        harness.Settings.Stored = Settings(protectionOn: false);

        var result = await harness.Service.TickAsync(loginStart: true);

        result.Action.Should().Be(WatchdogTickAction.StartedAppAtLogin);
        result.ProcessId.Should().Be(4321);
        harness.Processes.StartCount.Should().Be(1);
    }

    [Fact]
    public async Task Tick_does_not_start_the_app_at_login_when_start_with_windows_is_off()
    {
        var harness = new Harness();
        harness.Settings.Stored = Settings(protectionOn: false, startWithWindows: false);

        var result = await harness.Service.TickAsync(loginStart: true);

        result.Action.Should().Be(WatchdogTickAction.Stop);
        harness.Processes.StartCount.Should().Be(0);
    }

    [Fact]
    public async Task Tick_starts_the_app_when_protection_is_on_without_a_heartbeat()
    {
        var harness = new Harness();
        harness.Settings.Stored = Settings(protectionOn: true);

        var result = await harness.Service.TickAsync(loginStart: true);

        result.Action.Should().Be(WatchdogTickAction.StartedApp);
        result.Reason.Should().Be(WatchdogRestartReason.Recovery);
        result.ProcessId.Should().Be(4321);
        harness.Processes.StartCount.Should().Be(1);
        harness.States.State!.RestartCount.Should().Be(1);
        harness.States.State.PendingProcessId.Should().Be(4321);
        harness.Audits.Entries.Should().ContainSingle(e =>
            e.EventType == AuditEventType.WatchdogRestart && e.Actor == AuditActor.System);
    }

    [Fact]
    public async Task Tick_audits_only_reason_attempt_and_pids()
    {
        var harness = new Harness();
        harness.Settings.Stored = Settings(protectionOn: true);

        await harness.Service.TickAsync(loginStart: false);

        var metadata = harness.Audits.Entries.Single().MetadataJson!;
        using var document = JsonDocument.Parse(metadata);
        document.RootElement.GetProperty("reason").GetString().Should().Be("Recovery");
        document.RootElement.GetProperty("attempt").GetInt32().Should().Be(1);
        document.RootElement.GetProperty("newPid").GetInt64().Should().Be(4321);

        metadata.Should().NotContainEquivalentOf("pin");
        metadata.Should().NotContainEquivalentOf("password");
    }

    [Fact]
    public async Task Tick_waits_while_the_app_heartbeat_is_fresh_and_alive()
    {
        var harness = new Harness();
        harness.Settings.Stored = Settings(protectionOn: true);
        harness.Heartbeats.Heartbeat = Heartbeat(Now);
        harness.Processes.AliveProcesses.Add(AppPid);

        var result = await harness.Service.TickAsync(loginStart: false);

        result.Action.Should().Be(WatchdogTickAction.Wait);
        harness.Processes.StartCount.Should().Be(0);
    }

    [Fact]
    public async Task Tick_detects_an_unexpected_exit_from_a_dead_heartbeat_process()
    {
        var harness = new Harness();
        harness.Settings.Stored = Settings(protectionOn: true);
        harness.Heartbeats.Heartbeat = Heartbeat(Now);

        var result = await harness.Service.TickAsync(loginStart: false);

        result.Action.Should().Be(WatchdogTickAction.StartedApp);
        result.Reason.Should().Be(WatchdogRestartReason.UnexpectedExit);
        harness.Processes.StartCount.Should().Be(1);
    }

    [Fact]
    public async Task Tick_waits_for_the_startup_grace_before_treating_a_stale_heartbeat_as_a_hang()
    {
        var harness = new Harness();
        harness.Settings.Stored = Settings(protectionOn: true);
        harness.Heartbeats.Heartbeat = Heartbeat(Now.AddSeconds(-40));
        harness.Processes.AliveProcesses.Add(AppPid);

        var first = await harness.Service.TickAsync(loginStart: false);
        first.Action.Should().Be(WatchdogTickAction.Wait);

        harness.Advance(TimeSpan.FromSeconds(10));
        var second = await harness.Service.TickAsync(loginStart: false);
        second.Action.Should().Be(WatchdogTickAction.Wait);

        harness.Advance(TimeSpan.FromSeconds(10));
        var third = await harness.Service.TickAsync(loginStart: false);
        third.Action.Should().Be(WatchdogTickAction.Wait);
        harness.Processes.Killed.Should().BeEmpty();

        harness.Advance(TimeSpan.FromSeconds(10));
        var fourth = await harness.Service.TickAsync(loginStart: false);

        fourth.Action.Should().Be(WatchdogTickAction.KilledHang);
        fourth.Reason.Should().Be(WatchdogRestartReason.Hang);
        harness.Processes.Killed.Should().Contain(AppPid);
        harness.States.State!.PendingReason.Should().Be(WatchdogRestartReason.Hang);
    }

    [Fact]
    public async Task Tick_skips_the_hang_kill_once_after_the_machine_resumes_from_sleep()
    {
        var harness = new Harness();
        harness.Settings.Stored = Settings(protectionOn: true);
        harness.Heartbeats.Heartbeat = Heartbeat(Now);
        harness.Processes.AliveProcesses.Add(AppPid);

        await harness.Service.TickAsync(loginStart: false);

        harness.Advance(TimeSpan.FromMinutes(1));
        var resumed = await harness.Service.TickAsync(loginStart: false);

        resumed.Action.Should().Be(WatchdogTickAction.Wait);
        harness.Processes.Killed.Should().BeEmpty();

        harness.Advance(TimeSpan.FromSeconds(5));
        var next = await harness.Service.TickAsync(loginStart: false);

        next.Action.Should().Be(WatchdogTickAction.KilledHang);
    }

    [Fact]
    public async Task Tick_reports_a_failed_kill_without_restarting_while_the_app_is_alive()
    {
        var harness = new Harness();
        harness.Settings.Stored = Settings(protectionOn: true);
        harness.Heartbeats.Heartbeat = Heartbeat(Now.AddSeconds(-40));
        harness.Processes.AliveProcesses.Add(AppPid);
        harness.Processes.KillSucceeds = false;

        await harness.Service.TickAsync(loginStart: false);
        harness.Advance(TimeSpan.FromSeconds(15));
        await harness.Service.TickAsync(loginStart: false);
        harness.Advance(TimeSpan.FromSeconds(15));
        var result = await harness.Service.TickAsync(loginStart: false);

        result.Action.Should().Be(WatchdogTickAction.Wait);
        harness.Processes.StartCount.Should().Be(0);
        harness.Logger.Errors.Should().Contain(error => error.Contains("hung", StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public async Task Tick_does_not_restart_after_a_clean_exit_marker()
    {
        var harness = new Harness();
        var settings = Settings(protectionOn: true);
        settings.RecordCleanShutdown(Now);
        harness.Settings.Stored = settings;
        harness.Heartbeats.Heartbeat = Heartbeat(Now);

        var result = await harness.Service.TickAsync(loginStart: false);

        result.Action.Should().Be(WatchdogTickAction.Stop);
        harness.Processes.StartCount.Should().Be(0);
    }

    [Fact]
    public async Task Tick_never_starts_a_duplicate_when_the_app_mutex_is_held()
    {
        var harness = new Harness();
        harness.Settings.Stored = Settings(protectionOn: true);
        harness.Processes.AppInstanceRunning = true;

        var result = await harness.Service.TickAsync(loginStart: false);

        result.Action.Should().Be(WatchdogTickAction.Wait);
        harness.Processes.StartCount.Should().Be(0);
    }

    [Fact]
    public async Task Tick_resets_the_restart_budget_when_protection_is_off()
    {
        var harness = new Harness();
        harness.Settings.Stored = Settings(protectionOn: false);
        harness.States.State = new WatchdogRestartState
        {
            RestartCount = 3,
            WindowStartedAtUtc = Now.AddMinutes(-1),
            LastRestartAtUtc = Now.AddMinutes(-1)
        };

        var result = await harness.Service.TickAsync(loginStart: false);

        result.Action.Should().Be(WatchdogTickAction.Stop);
        harness.States.State!.RestartCount.Should().Be(0);
        harness.Processes.StartCount.Should().Be(0);
    }

    [Fact]
    public async Task Tick_obeys_the_backoff_window_between_restarts()
    {
        var harness = new Harness();
        harness.Settings.Stored = Settings(protectionOn: true);
        harness.Heartbeats.Heartbeat = Heartbeat(Now.AddMinutes(-1));
        harness.States.State = new WatchdogRestartState
        {
            RestartCount = 1,
            WindowStartedAtUtc = Now.AddMinutes(-1),
            LastRestartAtUtc = Now.AddMinutes(-1),
            NextRestartAtUtc = Now.AddSeconds(10)
        };

        var result = await harness.Service.TickAsync(loginStart: false);

        result.Action.Should().Be(WatchdogTickAction.Wait);
        result.Delay.Should().BeGreaterThan(TimeSpan.Zero).And.BeLessThanOrEqualTo(TimeSpan.FromSeconds(10));
        harness.Processes.StartCount.Should().Be(0);
    }

    [Fact]
    public async Task Tick_treats_a_dead_pending_start_as_an_unexpected_exit_and_backs_off()
    {
        var harness = new Harness();
        harness.Settings.Stored = Settings(protectionOn: true);
        harness.Heartbeats.Heartbeat = Heartbeat(Now.AddMinutes(-1));
        harness.States.State = new WatchdogRestartState
        {
            RestartCount = 1,
            WindowStartedAtUtc = Now.AddMinutes(-1),
            LastRestartAtUtc = Now.AddSeconds(-30),
            NextRestartAtUtc = Now.AddSeconds(-1),
            PendingProcessId = 4321,
            PendingProcessStartedAtUtc = Now.AddSeconds(-30)
        };

        var result = await harness.Service.TickAsync(loginStart: false);

        result.Action.Should().Be(WatchdogTickAction.StartedApp);
        result.Reason.Should().Be(WatchdogRestartReason.UnexpectedExit);
        harness.States.State!.RestartCount.Should().Be(2);
        harness.States.State.NextRestartAtUtc.Should().Be(Now.AddSeconds(10));
    }

    [Fact]
    public async Task Tick_gives_up_after_the_budget_and_audits_once()
    {
        var harness = new Harness();
        harness.Settings.Stored = Settings(protectionOn: true);
        harness.States.State = new WatchdogRestartState
        {
            RestartCount = 5,
            WindowStartedAtUtc = Now.AddMinutes(-1),
            LastRestartAtUtc = Now.AddMinutes(-1)
        };

        var first = await harness.Service.TickAsync(loginStart: false);

        first.Action.Should().Be(WatchdogTickAction.GaveUp);
        harness.Processes.StartCount.Should().Be(0);
        harness.Audits.Entries.Should().ContainSingle(e => e.EventType == AuditEventType.WatchdogGaveUp);
        harness.States.State!.GaveUpUntilUtc.Should().Be(Now.AddMinutes(9));

        var second = await harness.Service.TickAsync(loginStart: false);

        second.Action.Should().Be(WatchdogTickAction.Stop);
        harness.Audits.Entries.Should().ContainSingle(e => e.EventType == AuditEventType.WatchdogGaveUp);
    }

    [Fact]
    public async Task Tick_resets_the_budget_after_a_stable_period()
    {
        var harness = new Harness();
        harness.Settings.Stored = Settings(protectionOn: true);
        harness.Heartbeats.Heartbeat = Heartbeat(Now);
        harness.Processes.AliveProcesses.Add(AppPid);
        harness.States.State = new WatchdogRestartState
        {
            RestartCount = 2,
            WindowStartedAtUtc = Now.AddMinutes(-11),
            LastRestartAtUtc = Now.AddMinutes(-11)
        };

        var result = await harness.Service.TickAsync(loginStart: false);

        result.Action.Should().Be(WatchdogTickAction.Wait);
        harness.States.State!.RestartCount.Should().Be(0);
    }

    [Fact]
    public async Task Tick_retries_after_a_failed_start_without_spending_the_budget()
    {
        var harness = new Harness();
        harness.Settings.Stored = Settings(protectionOn: true);
        harness.Processes.StartResult = null;

        var result = await harness.Service.TickAsync(loginStart: false);

        result.Action.Should().Be(WatchdogTickAction.Wait);
        result.Delay.Should().Be(TimeSpan.FromMinutes(1));
        harness.Processes.StartCount.Should().Be(1);
        harness.States.State.Should().BeNull();
        harness.Audits.Entries.Should().BeEmpty();
    }

    [Fact]
    public async Task Tick_still_restarts_when_the_audit_write_fails()
    {
        var harness = new Harness();
        harness.Settings.Stored = Settings(protectionOn: true);
        harness.Audits.ThrowOnAdd = true;

        var result = await harness.Service.TickAsync(loginStart: false);

        result.Action.Should().Be(WatchdogTickAction.StartedApp);
        harness.Processes.StartCount.Should().Be(1);
        harness.Logger.Errors.Should().Contain(error => error.Contains("audit", StringComparison.OrdinalIgnoreCase));
    }

    private static AppSettings Settings(
        bool protectionOn,
        bool startWithWindows = true,
        bool onboarding = true)
    {
        var settings = AppSettings.CreateDefault(Now);

        if (onboarding)
        {
            settings.RecordPrivacyConsent(Now);
            settings.CompleteOnboarding(Now);
        }

        if (!startWithWindows)
            settings.UpdateStartWithWindows(false, Now);

        if (protectionOn)
            settings.EnableProtection(Now);

        return settings;
    }

    private static WatchdogHeartbeat Heartbeat(DateTime updatedAtUtc) =>
        new(AppPid, Now.AddMinutes(-5), updatedAtUtc);

    private sealed class Harness
    {
        public Harness()
        {
            Monotonic.Ticks = TimeSpan.FromMinutes(10);
            Service = new WatchdogService(
                Settings,
                Heartbeats,
                States,
                Processes,
                Audits,
                new TestCorrelationContext { Current = Guid.NewGuid() },
                Logger,
                Time,
                Monotonic,
                Options);
        }

        public FakeAppSettingsRepository Settings { get; } = new();

        public FakeWatchdogHeartbeatStore Heartbeats { get; } = new();

        public FakeWatchdogStateStore States { get; } = new();

        public FakeWatchdogProcessPort Processes { get; } = new();

        public RecordingAuditLogRepository Audits { get; } = new();

        public RecordingLogger Logger { get; } = new();

        public ManualTimeProvider Time { get; } = new(new DateTimeOffset(Now, TimeSpan.Zero));

        public FakeMonotonicClock Monotonic { get; } = new();

        public WatchdogOptions Options { get; } = new();

        public WatchdogService Service { get; }

        public void Advance(TimeSpan delta)
        {
            Time.Advance(delta);
            Monotonic.Ticks += delta;
        }
    }
}
