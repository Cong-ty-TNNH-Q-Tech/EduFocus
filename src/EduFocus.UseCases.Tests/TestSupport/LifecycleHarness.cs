using EduFocus.Domain.Entities;
using EduFocus.UseCases.Auth;
using EduFocus.UseCases.Lifecycle;
using EduFocus.UseCases.Protection;

namespace EduFocus.UseCases.Tests.TestSupport;

internal sealed class LifecycleHarness
{
    public const string Pin = "482915";

    public static readonly DateTimeOffset Start = new(2026, 10, 29, 8, 0, 0, TimeSpan.Zero);
    public static readonly Guid CorrelationId = Guid.Parse("22222222-3333-4444-5555-666666666666");

    public LifecycleHarness()
    {
        Clock = new ManualTimeProvider(Start, TimeZoneInfo.Utc);
        Correlation = new TestCorrelationContext { Current = CorrelationId };
        Session = new ParentSessionState(Clock);
        Settings.Stored = AppSettings.CreateDefault(Start.UtcDateTime);
        Auth = new ParentAuthUseCase(Credentials, Hasher, RecoveryCodes, Audits, Correlation, Clock);
        ParentMode = new ParentModeService(Auth, Session);
        Protection = new ProtectionService(
            Settings,
            Sessions,
            ScheduleWindows,
            AttentionEvents,
            Engine,
            Heartbeat,
            Activity,
            Session,
            Audits,
            Correlation,
            Logger,
            Clock);
        Lifecycle = new AppLifecycleService(
            ParentMode,
            Protection,
            Sessions,
            Heartbeat,
            Activity,
            Settings,
            Audits,
            Correlation,
            Logger,
            Clock);
    }

    public FakeAppSettingsRepository Settings { get; } = new();

    public FakeStudySessionRepository Sessions { get; } = new();

    public FakeScheduleWindowRepository ScheduleWindows { get; } = new();

    public FakeAttentionEventRepository AttentionEvents { get; } = new();

    public FakeProtectionEngine Engine { get; } = new();

    public FakeStudySessionHeartbeatUseCase Heartbeat { get; } = new();

    public FakeActivityUseCase Activity { get; } = new();

    public RecordingAuditLogRepository Audits { get; } = new();

    public RecordingLogger Logger { get; } = new();

    public FakeParentCredentialRepository Credentials { get; } = new();

    public FakePasswordHasher Hasher { get; } = new();

    public FakeRecoveryCodeGenerator RecoveryCodes { get; } = new("ABCDEFGHJKLM");

    public TestCorrelationContext Correlation { get; }

    public ManualTimeProvider Clock { get; }

    public ParentSessionState Session { get; }

    public ParentAuthUseCase Auth { get; }

    public ParentModeService ParentMode { get; }

    public ProtectionService Protection { get; }

    public AppLifecycleService Lifecycle { get; }

    public AppSettings SettingsRow => Settings.Stored!;

    public static async Task<LifecycleHarness> WithPinAsync()
    {
        var harness = new LifecycleHarness();
        await harness.Auth.SetupAsync(Pin);
        return harness;
    }

    public async Task EnableProtectionAsync()
    {
        Session.Authenticate();
        await Protection.EnableAsync();
        Session.Lock();
    }
}
