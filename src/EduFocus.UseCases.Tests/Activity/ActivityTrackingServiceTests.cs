using EduFocus.Domain.Entities;
using EduFocus.Domain.Enums;
using EduFocus.Domain.ValueObjects;
using EduFocus.Ports.Outbound.Services;
using EduFocus.UseCases.Activity;
using EduFocus.UseCases.Tests.TestSupport;
using FluentAssertions;

namespace EduFocus.UseCases.Tests.Activity;

public sealed class ActivityTrackingServiceTests
{
    private static readonly DateTime Now = new(2026, 10, 30, 8, 0, 0, DateTimeKind.Utc);

    [Fact]
    public async Task Fast_foreground_changes_are_not_sampled_until_a_context_is_stable()
    {
        await using var harness = new ActivityHarness();
        await harness.StartAsync();

        harness.Foreground = harness.Snapshot("chrome", "Docs");
        (await harness.SampleAfterAsync(TimeSpan.FromSeconds(15))).Should().BeFalse();

        harness.Foreground = harness.Snapshot("firefox", "Video");
        (await harness.SampleAfterAsync(TimeSpan.FromSeconds(1))).Should().BeFalse();

        harness.Foreground = harness.Snapshot("chrome", "Docs");
        (await harness.SampleAfterAsync(TimeSpan.FromSeconds(1))).Should().BeFalse();

        (await harness.SampleAfterAsync(TimeSpan.FromSeconds(1))).Should().BeFalse();
        harness.Samples.Samples.Should().BeEmpty();

        (await harness.SampleAfterAsync(TimeSpan.FromSeconds(1))).Should().BeTrue();

        var sample = harness.Samples.Samples.Should().ContainSingle().Subject;
        sample.ProcessName.Should().Be("chrome");
        sample.DurationSeconds.Should().BeGreaterThanOrEqualTo(15);
    }

    [Fact]
    public async Task Stable_foreground_writes_session_counters_and_sample()
    {
        await using var harness = new ActivityHarness();
        await harness.StartAsync();

        harness.Foreground = harness.Snapshot("chrome", "Docs");

        (await harness.SampleAfterAsync(TimeSpan.FromSeconds(15))).Should().BeFalse();
        (await harness.SampleAfterAsync(TimeSpan.FromSeconds(2))).Should().BeTrue();

        var sample = harness.Samples.Samples.Should().ContainSingle().Subject;
        sample.SessionId.Should().Be(harness.Session.Id);
        sample.SampledAtUtc.Should().Be(Now.AddSeconds(17));
        sample.DurationSeconds.Should().Be(17);
        sample.Category.Should().Be(ActivityCategory.Neutral);
        sample.IsForeground.Should().BeTrue();
        sample.AppName.Should().Be("Google Chrome");
        sample.ProcessName.Should().Be("chrome");
        sample.Url.Should().BeNull();
        sample.UrlSource.Should().Be(UrlSource.None);
        sample.IdleSeconds.Should().Be(0);

        harness.Session.NeutralSeconds.Should().Be(17);

        (await harness.SampleOnceAsync()).Should().BeFalse();
        harness.Samples.Samples.Should().ContainSingle();
    }

    [Fact]
    public async Task Idle_above_the_threshold_writes_one_sample_and_suppresses_repeats()
    {
        await using var harness = new ActivityHarness();
        await harness.StartAsync();

        harness.Foreground = harness.Snapshot("chrome", "Docs", idleSeconds: 301);

        (await harness.SampleAfterAsync(TimeSpan.FromSeconds(15))).Should().BeTrue();

        var idleSample = harness.Samples.Samples.Should().ContainSingle().Subject;
        idleSample.Category.Should().Be(ActivityCategory.Neutral);
        idleSample.DurationSeconds.Should().Be(15);
        idleSample.IdleSeconds.Should().Be(15);
        harness.Session.IdleSeconds.Should().Be(15);
        harness.Session.NeutralSeconds.Should().Be(0);

        (await harness.SampleAfterAsync(TimeSpan.FromSeconds(15))).Should().BeFalse();
        (await harness.SampleAfterAsync(TimeSpan.FromSeconds(15))).Should().BeFalse();
        harness.Samples.Samples.Should().ContainSingle();

        harness.Foreground = harness.Snapshot("chrome", "Docs");
        (await harness.SampleAfterAsync(TimeSpan.FromSeconds(15))).Should().BeTrue();

        harness.Samples.Samples.Should().HaveCount(2);
        harness.Samples.Samples[1].IdleSeconds.Should().Be(0);
        harness.Session.NeutralSeconds.Should().Be(45);
    }

    [Fact]
    public async Task Samples_without_url_still_record_the_app_and_process()
    {
        await using var harness = new ActivityHarness();
        await harness.StartAsync();

        harness.Foreground = harness.Snapshot("chrome", "Docs");
        harness.Urls.Url = null;

        await harness.WriteSampleAsync();

        var sample = harness.Samples.Samples.Should().ContainSingle().Subject;
        sample.Url.Should().BeNull();
        sample.UrlSource.Should().Be(UrlSource.None);
        sample.AppName.Should().Be("Google Chrome");
        sample.ProcessName.Should().Be("chrome");
    }

    [Fact]
    public async Task Url_is_sanitized_and_online_class_detected()
    {
        await using var harness = new ActivityHarness();
        await harness.StartAsync();

        harness.Foreground = harness.Snapshot("chrome", "Meet\u0001 - abc");
        harness.Urls.Url = "https://meet.google.com/abc-def-ghi?authuser=0&token=secret#section";

        await harness.WriteSampleAsync();

        var sample = harness.Samples.Samples.Should().ContainSingle().Subject;
        sample.Url.Should().Be("https://meet.google.com/abc-def-ghi");
        sample.UrlSource.Should().Be(UrlSource.UIA);
        sample.Domain.Should().Be(DomainName.Parse("meet.google.com"));
        sample.IsOnlineClass.Should().BeTrue();
        sample.Category.Should().Be(ActivityCategory.Study);
        sample.WindowTitle.Should().Be("Meet  - abc");
        sample.Url.Should().NotContain("token");
        sample.Url.Should().NotContain("#");

        harness.Session.StudySeconds.Should().Be(17);
        harness.Session.OnlineClassSeconds.Should().Be(17);
    }

    [Fact]
    public async Task Teams_process_marks_study_time_without_duplicating_counters()
    {
        await using var harness = new ActivityHarness();
        await harness.StartAsync();

        harness.Foreground = harness.Snapshot("ms-teams", "Microsoft Teams");
        harness.Urls.Url = null;

        await harness.WriteSampleAsync();

        var sample = harness.Samples.Samples.Should().ContainSingle().Subject;
        sample.IsOnlineClass.Should().BeTrue();
        sample.Category.Should().Be(ActivityCategory.Study);
        sample.UrlSource.Should().Be(UrlSource.None);
        harness.Session.StudySeconds.Should().Be(17);
        harness.Session.OnlineClassSeconds.Should().Be(17);
        harness.Session.NeutralSeconds.Should().Be(0);
    }

    [Fact]
    public async Task Multilingual_meet_title_is_detected_without_a_url()
    {
        await using var harness = new ActivityHarness();
        await harness.StartAsync();

        harness.Foreground = harness.Snapshot("chrome", "Google Meet – Lớp Vật lý");
        harness.Urls.Url = null;

        await harness.WriteSampleAsync();

        var sample = harness.Samples.Samples.Should().ContainSingle().Subject;
        sample.IsOnlineClass.Should().BeTrue();
        sample.Category.Should().Be(ActivityCategory.Study);
        harness.Session.OnlineClassSeconds.Should().Be(17);
    }

    [Fact]
    public async Task Chat_app_is_not_marked_as_an_online_class()
    {
        await using var harness = new ActivityHarness();
        await harness.StartAsync();

        harness.Foreground = harness.Snapshot("discord", "Discord | friends");

        await harness.WriteSampleAsync();

        var sample = harness.Samples.Samples.Should().ContainSingle().Subject;
        sample.IsOnlineClass.Should().BeFalse();
        sample.Category.Should().Be(ActivityCategory.Neutral);
        harness.Session.NeutralSeconds.Should().Be(17);
        harness.Session.OnlineClassSeconds.Should().Be(0);
    }

    [Fact]
    public async Task Eligible_time_accrues_for_the_whole_session_when_monitoring_is_always_on()
    {
        await using var harness = new ActivityHarness();
        await harness.StartAsync();

        harness.Foreground = harness.Snapshot("discord", "Discord | friends");

        await harness.WriteSampleAsync();

        harness.Session.MonitoringEligibleSeconds.Should().Be(17);
        harness.Session.MonitoredSeconds.Should().Be(0);
        harness.Session.CoveragePercent.Should().Be(0);
        harness.Sessions.ActivityWrites.Should().ContainSingle()
            .Which.MonitoringEligibleSeconds.Should().Be(17);
    }

    [Fact]
    public async Task Monitor_only_during_classes_counts_only_online_class_windows()
    {
        await using var harness = new ActivityHarness();
        harness.Settings.Stored = AppSettings.CreateDefault(Now);
        harness.SettingsRow.UpdateMonitoring(monitoringEnabled: true, monitorOnlyDuringClasses: true, Now);
        await harness.StartAsync();

        harness.Foreground = harness.Snapshot("discord", "Discord | friends");
        await harness.WriteSampleAsync();
        harness.Session.MonitoringEligibleSeconds.Should().Be(0);

        harness.Foreground = harness.Snapshot("chrome", "Google Meet – Lớp Vật lý");
        await harness.WriteSampleAsync();

        harness.Session.MonitoringEligibleSeconds.Should().Be(17);
        harness.Session.OnlineClassSeconds.Should().Be(17);
    }

    [Fact]
    public async Task Idle_time_is_eligible_when_monitoring_is_not_limited_to_classes()
    {
        await using var harness = new ActivityHarness();
        await harness.StartAsync();

        harness.Foreground = harness.Snapshot("chrome", "Docs", idleSeconds: 301);

        (await harness.SampleAfterAsync(TimeSpan.FromSeconds(15))).Should().BeTrue();

        harness.Session.IdleSeconds.Should().Be(15);
        harness.Session.MonitoringEligibleSeconds.Should().Be(15);
    }

    [Fact]
    public async Task Disabled_monitoring_does_not_accrue_eligible_time()
    {
        await using var harness = new ActivityHarness();
        harness.SettingsRow.UpdateMonitoring(monitoringEnabled: false, monitorOnlyDuringClasses: false, Now);
        await harness.StartAsync();

        harness.Foreground = harness.Snapshot("chrome", "Docs");

        await harness.WriteSampleAsync();

        harness.Session.MonitoringEligibleSeconds.Should().Be(0);
    }

    [Fact]
    public async Task Idle_class_window_is_not_marked_as_online_class()
    {
        await using var harness = new ActivityHarness();
        await harness.StartAsync();

        harness.Foreground = harness.Snapshot("chrome", "Meet - abc-defg-hij", idleSeconds: 301);

        (await harness.SampleAfterAsync(TimeSpan.FromSeconds(15))).Should().BeTrue();

        var sample = harness.Samples.Samples.Should().ContainSingle().Subject;
        sample.IsOnlineClass.Should().BeFalse();
        sample.IdleSeconds.Should().Be(15);
        harness.Session.StudySeconds.Should().Be(0);
        harness.Session.OnlineClassSeconds.Should().Be(0);
        harness.Session.IdleSeconds.Should().Be(15);
    }

    [Fact]
    public async Task Blocked_domain_is_distraction()
    {
        await using var harness = new ActivityHarness();
        harness.Rules.Rules.Add(BlockRule.Create(
            RuleType.Domain,
            RuleMatchType.Exact,
            "chatgpt.com",
            BlockRuleCategory.Chatbot,
            BlockRuleEffect.Block,
            Now));

        await harness.StartAsync();

        harness.Foreground = harness.Snapshot("chrome", "ChatGPT");
        harness.Urls.Url = "https://chatgpt.com/c/123";

        await harness.WriteSampleAsync();

        var sample = harness.Samples.Samples.Should().ContainSingle().Subject;
        sample.Category.Should().Be(ActivityCategory.Distraction);
        harness.Session.DistractionSeconds.Should().Be(17);
        harness.Session.StudySeconds.Should().Be(0);
    }

    [Fact]
    public async Task Foreground_failure_writes_unknown_and_logs_once()
    {
        await using var harness = new ActivityHarness();
        await harness.StartAsync();

        harness.ForegroundPort.ExceptionToThrow = new UnauthorizedAccessException("denied");

        (await harness.SampleAfterAsync(TimeSpan.FromSeconds(15))).Should().BeFalse();
        (await harness.SampleAfterAsync(TimeSpan.FromSeconds(2))).Should().BeTrue();
        (await harness.SampleAfterAsync(TimeSpan.FromSeconds(15))).Should().BeTrue();

        harness.Samples.Samples.Should().HaveCount(2);
        harness.Samples.Samples[0].Category.Should().Be(ActivityCategory.Unknown);
        harness.Samples.Samples[0].IsForeground.Should().BeFalse();
        harness.Logger.Warnings.Should().ContainSingle()
            .Which.Should().Contain("Foreground activity is unavailable");
    }

    [Fact]
    public async Task Disappeared_process_still_produces_a_sample()
    {
        await using var harness = new ActivityHarness();
        await harness.StartAsync();

        harness.Foreground = new ForegroundActivitySnapshot(
            ProcessId: 4321,
            WindowHandle: 99,
            ProcessName: null,
            AppName: null,
            WindowTitle: null,
            IdleSeconds: 0);

        await harness.WriteSampleAsync();

        var sample = harness.Samples.Samples.Should().ContainSingle().Subject;
        sample.ProcessName.Should().BeNull();
        sample.AppName.Should().BeNull();
        sample.Category.Should().Be(ActivityCategory.Unknown);
    }

    [Fact]
    public async Task Url_provider_failure_keeps_the_app_details()
    {
        await using var harness = new ActivityHarness();
        await harness.StartAsync();

        harness.Foreground = harness.Snapshot("chrome", "Docs");
        harness.Urls.ExceptionToThrow = new InvalidOperationException("UIA unavailable");

        await harness.WriteSampleAsync();

        var sample = harness.Samples.Samples.Should().ContainSingle().Subject;
        sample.Url.Should().BeNull();
        sample.UrlSource.Should().Be(UrlSource.None);
        sample.ProcessName.Should().Be("chrome");
        harness.Logger.Warnings.Should().ContainSingle()
            .Which.Should().Contain("Browser URL detection is unavailable");
    }

    [Fact]
    public async Task Database_failures_are_logged_and_retried_on_a_later_beat()
    {
        await using var harness = new ActivityHarness();
        await harness.StartAsync();

        harness.Foreground = harness.Snapshot("chrome", "Docs");
        harness.Sessions.ThrowOnActivity = true;

        (await harness.SampleAfterAsync(TimeSpan.FromSeconds(15))).Should().BeFalse();
        (await harness.SampleAfterAsync(TimeSpan.FromSeconds(2))).Should().BeFalse();

        harness.Samples.Samples.Should().BeEmpty();
        harness.Logger.Warnings.Should().Contain(warning =>
            warning.Contains("Could not persist an activity sample"));

        harness.Sessions.ThrowOnActivity = false;

        (await harness.SampleAfterAsync(TimeSpan.FromSeconds(15))).Should().BeTrue();

        harness.Samples.Samples.Should().ContainSingle();
        harness.Session.NeutralSeconds.Should().Be(15);
    }

    [Fact]
    public async Task Completed_session_stops_counting_and_sampling()
    {
        await using var harness = new ActivityHarness();
        await harness.StartAsync();

        harness.Foreground = harness.Snapshot("chrome", "Docs");
        await harness.WriteSampleAsync();

        harness.Session.Complete(harness.Session.UpdatedAtUtc);
        harness.Samples.Samples.Should().ContainSingle();

        (await harness.SampleAfterAsync(TimeSpan.FromSeconds(15))).Should().BeFalse();
        (await harness.SampleAfterAsync(TimeSpan.FromSeconds(15))).Should().BeFalse();

        harness.Samples.Samples.Should().ContainSingle();
        harness.Session.NeutralSeconds.Should().Be(17);
    }

    [Fact]
    public async Task Suspension_gap_is_discarded_and_sampling_resumes()
    {
        await using var harness = new ActivityHarness();
        await harness.StartAsync();

        harness.Foreground = harness.Snapshot("chrome", "Docs");

        (await harness.SampleAfterAsync(TimeSpan.FromMinutes(30))).Should().BeFalse();
        harness.Samples.Samples.Should().BeEmpty();
        harness.Session.NeutralSeconds.Should().Be(0);

        (await harness.SampleAfterAsync(TimeSpan.FromSeconds(15))).Should().BeTrue();

        harness.Samples.Samples.Should().ContainSingle()
            .Which.DurationSeconds.Should().Be(15);
        harness.Session.NeutralSeconds.Should().Be(15);
    }

    [Fact]
    public async Task Suspend_and_resume_restart_the_sample_window()
    {
        await using var harness = new ActivityHarness();
        await harness.StartAsync();

        harness.Foreground = harness.Snapshot("chrome", "Docs");
        await harness.WriteSampleAsync();

        harness.Power.RaiseSuspended();
        harness.Clock.Advance(TimeSpan.FromMinutes(10));
        harness.Monotonic.Ticks += TimeSpan.FromMinutes(10);
        harness.Power.RaiseResumed();

        (await harness.SampleOnceAsync()).Should().BeFalse();
        harness.Samples.Samples.Should().ContainSingle();
        harness.Session.NeutralSeconds.Should().Be(17);

        (await harness.SampleAfterAsync(TimeSpan.FromSeconds(15))).Should().BeFalse();
        (await harness.SampleAfterAsync(TimeSpan.FromSeconds(2))).Should().BeTrue();

        harness.Samples.Samples.Should().HaveCount(2);
        harness.Samples.Samples[1].DurationSeconds.Should().Be(17);
        harness.Session.NeutralSeconds.Should().Be(34);
    }

    [Fact]
    public async Task Forward_wall_clock_jump_does_not_inflate_sample_duration()
    {
        await using var harness = new ActivityHarness();
        await harness.StartAsync();

        harness.Foreground = harness.Snapshot("chrome", "Docs");
        harness.Clock.Advance(TimeSpan.FromHours(2));
        harness.Monotonic.Ticks += TimeSpan.FromSeconds(15);

        (await harness.SampleOnceAsync()).Should().BeFalse();
        (await harness.SampleAfterAsync(TimeSpan.FromSeconds(2))).Should().BeTrue();

        var sample = harness.Samples.Samples.Should().ContainSingle().Subject;
        sample.DurationSeconds.Should().Be(17);
        sample.SampledAtUtc.Should().Be(Now.AddHours(2).AddSeconds(2));
        harness.Session.NeutralSeconds.Should().Be(17);
    }

    [Fact]
    public async Task Backward_wall_clock_keeps_sample_timestamps_monotonic()
    {
        await using var harness = new ActivityHarness();
        await harness.StartAsync();

        harness.Foreground = harness.Snapshot("chrome", "Docs");
        await harness.WriteSampleAsync();

        harness.Clock.Advance(TimeSpan.FromSeconds(-10));
        harness.Monotonic.Ticks += TimeSpan.FromSeconds(15);

        (await harness.SampleOnceAsync()).Should().BeTrue();

        harness.Samples.Samples.Should().HaveCount(2);
        harness.Samples.Samples[1].SampledAtUtc.Should().Be(Now.AddSeconds(17));
        harness.Samples.Samples[1].DurationSeconds.Should().Be(15);
    }

    [Fact]
    public async Task Loop_starts_and_stops_with_the_lifecycle()
    {
        await using var harness = new ActivityHarness();

        (await harness.SampleOnceAsync()).Should().BeFalse();
        harness.Samples.Samples.Should().BeEmpty();

        await harness.StartAsync();
        harness.Service.IsRunning.Should().BeTrue();

        await harness.Service.StopAsync();
        harness.Service.IsRunning.Should().BeFalse();
    }

    private sealed class ActivityHarness : IAsyncDisposable
    {
        public ActivityHarness()
        {
            Session = StudySession.Start(
                Now,
                new DateOnly(2026, 10, 30),
                SessionTrigger.Manual,
                monitoringEnabled: true);

            Sessions.Sessions.Add(Session);
            Settings.Stored = AppSettings.CreateDefault(Now);
            Service = new ActivityTrackingService(
                Samples,
                Sessions,
                Settings,
                Rules,
                ForegroundPort,
                Urls,
                Monotonic,
                Power,
                Logger,
                Clock,
                new ActivityTrackingOptions
                {
                    PollInterval = TimeSpan.FromMinutes(1),
                    SampleInterval = TimeSpan.FromSeconds(15)
                });
        }

        public ManualTimeProvider Clock { get; } = new(new DateTimeOffset(Now), TimeZoneInfo.Utc);

        public FakeMonotonicClock Monotonic { get; } = new();

        public FakeSystemPowerEventPort Power { get; } = new();

        public FakeStudySessionRepository Sessions { get; } = new();

        public FakeAppSettingsRepository Settings { get; } = new();

        public AppSettings SettingsRow => Settings.Stored!;

        public FakeActivitySampleRepository Samples { get; } = new();

        public FakeBlockRuleRepository Rules { get; } = new();

        public FakeForegroundActivityPort ForegroundPort { get; } = new();

        public FakeBrowserUrlProvider Urls { get; } = new();

        public RecordingLogger Logger { get; } = new();

        public StudySession Session { get; }

        public ActivityTrackingService Service { get; }

        public ForegroundActivitySnapshot? Foreground
        {
            get => ForegroundPort.Snapshot;
            set => ForegroundPort.Snapshot = value;
        }

        public Task StartAsync() => Service.StartAsync(Session.Id);

        public Task<bool> SampleOnceAsync() => Service.SampleOnceAsync();

        public async Task<bool> SampleAfterAsync(TimeSpan delta)
        {
            Clock.Advance(delta);
            Monotonic.Ticks += delta;
            return await Service.SampleOnceAsync();
        }

        public async Task WriteSampleAsync()
        {
            (await SampleAfterAsync(TimeSpan.FromSeconds(15))).Should().BeFalse();
            (await SampleAfterAsync(TimeSpan.FromSeconds(2))).Should().BeTrue();
        }

        public ForegroundActivitySnapshot Snapshot(
            string processName,
            string windowTitle,
            int idleSeconds = 0) =>
            new(
                ProcessId: 1234,
                WindowHandle: 42,
                ProcessName: processName,
                AppName: "Google Chrome",
                WindowTitle: windowTitle,
                IdleSeconds: idleSeconds);

        public ValueTask DisposeAsync() => Service.DisposeAsync();
    }
}
