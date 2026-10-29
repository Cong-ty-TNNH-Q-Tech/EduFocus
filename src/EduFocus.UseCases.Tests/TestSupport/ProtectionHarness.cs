using EduFocus.Domain.Entities;
using EduFocus.UseCases.Auth;
using EduFocus.UseCases.Protection;

namespace EduFocus.UseCases.Tests.TestSupport;

internal sealed class ProtectionHarness
{
    public static readonly DateTimeOffset Start = new(2026, 10, 29, 8, 0, 0, TimeSpan.Zero);
    public static readonly Guid CorrelationId = Guid.Parse("aaaaaaaa-bbbb-cccc-dddd-eeeeeeeeeeee");

    public ProtectionHarness()
    {
        Clock = new ManualTimeProvider(Start);
        Correlation = new TestCorrelationContext { Current = CorrelationId };
        Session = new ParentSessionState(Clock);
        Settings.Stored = AppSettings.CreateDefault(Start.UtcDateTime);
        Service = CreateService();
    }

    public FakeAppSettingsRepository Settings { get; } = new();

    public FakeStudySessionRepository Sessions { get; } = new();

    public FakeProtectionEngine Engine { get; } = new();

    public RecordingAuditLogRepository Audits { get; } = new();

    public RecordingLogger Logger { get; } = new();

    public TestCorrelationContext Correlation { get; }

    public ManualTimeProvider Clock { get; }

    public ParentSessionState Session { get; }

    public ProtectionService Service { get; }

    public AppSettings SettingsRow => Settings.Stored!;

    public ProtectionService CreateService() =>
        new(Settings, Sessions, Engine, Session, Audits, Correlation, Logger, Clock);
}
