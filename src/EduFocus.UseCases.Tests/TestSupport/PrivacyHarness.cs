using EduFocus.Domain.Entities;
using EduFocus.UseCases.Auth;
using EduFocus.UseCases.Privacy;

namespace EduFocus.UseCases.Tests.TestSupport;

internal sealed class PrivacyHarness
{
    public const string CorrectPin = AuthHarness.InitialPin;

    private PrivacyHarness(AuthHarness auth, bool authenticated, int batchSize)
    {
        Auth = auth;
        Clock = auth.Clock;
        Settings = new FakeAppSettingsRepository
        {
            Stored = AppSettings.CreateDefault(AuthHarness.Start.UtcDateTime)
        };
        Data = new FakePrivacyDataRepository();
        Session = new ParentSessionState(Clock);

        if (authenticated)
            Session.Authenticate();

        Service = new PrivacySettingsService(
            Settings,
            Data,
            Session,
            Audits,
            Correlation,
            Clock);

        Deletion = new DataDeletionService(
            Data,
            auth.UseCase,
            Session,
            Audits,
            Correlation,
            Logger,
            Clock,
            new DataDeletionOptions { BatchSize = batchSize });
    }

    public static async Task<PrivacyHarness> CreateAsync(
        bool authenticated = true,
        bool configurePin = true,
        int batchSize = 2)
    {
        var auth = configurePin ? await AuthHarness.WithCredentialAsync() : new AuthHarness();
        return new PrivacyHarness(auth, authenticated, batchSize);
    }

    public AuthHarness Auth { get; }

    public ManualTimeProvider Clock { get; }

    public ParentSessionState Session { get; }

    public FakeAppSettingsRepository Settings { get; }

    public FakePrivacyDataRepository Data { get; }

    public RecordingAuditLogRepository Audits { get; } = new();

    public RecordingLogger Logger { get; } = new();

    public TestCorrelationContext Correlation { get; } = new();

    public PrivacySettingsService Service { get; }

    public DataDeletionService Deletion { get; }
}
