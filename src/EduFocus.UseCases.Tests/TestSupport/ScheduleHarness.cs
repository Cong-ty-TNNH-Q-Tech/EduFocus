using EduFocus.Domain.Entities;
using EduFocus.Domain.Enums;
using EduFocus.UseCases.Auth;
using EduFocus.UseCases.Protection;
using EduFocus.UseCases.Schedule;

namespace EduFocus.UseCases.Tests.TestSupport;

internal sealed class ScheduleHarness
{
    public static readonly DateTimeOffset Start = new(2026, 10, 29, 8, 0, 0, TimeSpan.Zero);

    public ScheduleHarness(TimeZoneInfo? localTimeZone = null, DateTimeOffset? start = null)
    {
        Clock = new ManualTimeProvider(start ?? Start, localTimeZone ?? TimeZoneInfo.Utc);
        Correlation = new TestCorrelationContext { Current = Guid.NewGuid() };
        Session = new ParentSessionState(Clock);
        Settings.Stored = AppSettings.CreateDefault(Clock.GetUtcNow().UtcDateTime);
        Protection = new ProtectionService(
            Settings,
            Sessions,
            ScheduleWindows,
            AttentionEvents,
            Engine,
            Heartbeat,
            Activity,
            AttentionPipeline,
            AttentionMonitor,
            Session,
            Audits,
            Correlation,
            Logger,
            Clock);
        Service = new ScheduleService(
            ScheduleWindows,
            Settings,
            Protection,
            Session,
            Audits,
            Correlation,
            Logger,
            Clock,
            Monotonic);
    }

    public FakeAppSettingsRepository Settings { get; } = new();

    public FakeStudySessionRepository Sessions { get; } = new();

    public FakeScheduleWindowRepository ScheduleWindows { get; } = new();

    public FakeAttentionEventRepository AttentionEvents { get; } = new();

    public FakeProtectionEngine Engine { get; } = new();

    public FakeStudySessionHeartbeatUseCase Heartbeat { get; } = new();

    public FakeActivityUseCase Activity { get; } = new();

    public FakeAttentionPipelineUseCase AttentionPipeline { get; } = new();

    public FakeAttentionMonitorUseCase AttentionMonitor { get; } = new();

    public FakeMonotonicClock Monotonic { get; } = new();

    public RecordingAuditLogRepository Audits { get; } = new();

    public RecordingLogger Logger { get; } = new();

    public TestCorrelationContext Correlation { get; }

    public ManualTimeProvider Clock { get; }

    public ParentSessionState Session { get; }

    public ProtectionService Protection { get; }

    public ScheduleService Service { get; }

    public AppSettings SettingsRow => Settings.Stored!;

    public void Authenticate() => Session.Authenticate();

    public ScheduleWindow SeedWindow(
        ScheduleDays days,
        int startMinutes,
        int endMinutes,
        bool isEnabled = true,
        string? label = null)
    {
        var window = ScheduleWindow.Create(
            days,
            startMinutes,
            endMinutes,
            label,
            Clock.GetUtcNow().UtcDateTime,
            isEnabled);

        ScheduleWindows.Windows.Add(window);
        return window;
    }
}
