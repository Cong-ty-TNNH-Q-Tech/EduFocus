using EduFocus.Domain.Entities;
using EduFocus.Ports.DTOs;
using EduFocus.UseCases.Tests.TestSupport;
using EduFocus.UseCases.Watchdog;
using FluentAssertions;

namespace EduFocus.UseCases.Tests.Watchdog;

public sealed class WatchdogCoordinatorTests
{
    private static readonly DateTime Now = new(2026, 10, 29, 8, 0, 0, DateTimeKind.Utc);

    [Fact]
    public async Task Incomplete_onboarding_registers_no_tasks_and_starts_nothing()
    {
        var harness = new Harness(Settings(protectionOn: false, onboarding: false));

        await harness.Coordinator.SynchronizeAsync();

        harness.AutoStart.Modes.Should().Equal(WatchdogRegistrationMode.None);
        harness.Runtime.EnsureStartedCount.Should().Be(0);
    }

    [Fact]
    public async Task Protection_on_registers_both_tasks_and_starts_the_watchdog()
    {
        var harness = new Harness(Settings(protectionOn: true));

        await harness.Coordinator.SynchronizeAsync();

        harness.AutoStart.Modes.Should().Equal(WatchdogRegistrationMode.LogonAndHealth);
        harness.Runtime.EnsureStartedCount.Should().Be(1);
        harness.Runtime.Running.Should().BeTrue();
    }

    [Fact]
    public async Task Protection_off_with_start_with_windows_registers_logon_only_and_stops_the_watchdog()
    {
        var harness = new Harness(Settings(protectionOn: false, startWithWindows: true));
        harness.Runtime.Running = true;

        await harness.Coordinator.SynchronizeAsync();

        harness.AutoStart.Modes.Should().Equal(WatchdogRegistrationMode.LogonOnly);
        harness.Runtime.StopCount.Should().Be(1);
        harness.Runtime.EnsureStartedCount.Should().Be(0);
    }

    [Fact]
    public async Task Protection_off_without_start_with_windows_registers_nothing()
    {
        var harness = new Harness(Settings(protectionOn: false, startWithWindows: false));
        harness.Runtime.Running = true;

        await harness.Coordinator.SynchronizeAsync();

        harness.AutoStart.Modes.Should().Equal(WatchdogRegistrationMode.None);
        harness.Runtime.StopCount.Should().Be(1);
        harness.Runtime.EnsureStartedCount.Should().Be(0);
    }

    [Fact]
    public async Task Repeating_the_same_mode_does_not_touch_the_task_scheduler_again()
    {
        var harness = new Harness(Settings(protectionOn: true));

        await harness.Coordinator.SynchronizeAsync();
        await harness.Coordinator.SynchronizeAsync();

        harness.AutoStart.Modes.Should().HaveCount(1);
        harness.Runtime.EnsureStartedCount.Should().Be(1);
    }

    [Fact]
    public async Task A_registration_failure_is_logged_and_does_not_block_the_watchdog()
    {
        var harness = new Harness(Settings(protectionOn: true));
        harness.AutoStart.ThrowOnSynchronize = new InvalidOperationException("schtasks failed");

        await harness.Coordinator.SynchronizeAsync();

        harness.Runtime.EnsureStartedCount.Should().Be(1);
        harness.Logger.Errors.Should().Contain(error => error.Contains("auto-start", StringComparison.OrdinalIgnoreCase));

        harness.Settings.Stored = Settings(protectionOn: false, startWithWindows: true);
        await harness.Coordinator.SynchronizeAsync();

        harness.AutoStart.Modes.Should().Equal(
            WatchdogRegistrationMode.LogonAndHealth,
            WatchdogRegistrationMode.LogonOnly);
    }

    [Fact]
    public async Task A_failed_stop_is_logged_but_not_thrown()
    {
        var harness = new Harness(Settings(protectionOn: false, startWithWindows: true));
        harness.Runtime.Running = true;
        harness.Runtime.StopResult = false;

        await harness.Coordinator.SynchronizeAsync();

        harness.Runtime.StopCount.Should().Be(1);
        harness.Logger.Warnings.Should().Contain(w => w.Contains("stop", StringComparison.OrdinalIgnoreCase));
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

    private sealed class Harness
    {
        public Harness(AppSettings settings)
        {
            Settings.Stored = settings;
            Coordinator = new WatchdogCoordinator(Settings, AutoStart, Runtime, Logger, new WatchdogOptions());
        }

        public FakeAppSettingsRepository Settings { get; } = new();

        public FakeWatchdogAutoStartPort AutoStart { get; } = new();

        public FakeWatchdogRuntimePort Runtime { get; } = new();

        public RecordingLogger Logger { get; } = new();

        public WatchdogCoordinator Coordinator { get; }
    }
}
