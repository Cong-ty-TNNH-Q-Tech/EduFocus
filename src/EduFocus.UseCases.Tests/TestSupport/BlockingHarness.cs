using EduFocus.Domain.Protection;
using EduFocus.UseCases.Auth;
using EduFocus.UseCases.Blocking;

namespace EduFocus.UseCases.Tests.TestSupport;

internal sealed class BlockingHarness
{
    public static readonly DateTimeOffset Start = new(2026, 10, 29, 8, 0, 0, TimeSpan.Zero);

    public BlockingHarness(BuiltInRuleCatalog? catalog = null)
    {
        Clock = new ManualTimeProvider(Start, TimeZoneInfo.Utc);
        Correlation = new TestCorrelationContext { Current = Guid.NewGuid() };
        Session = new ParentSessionState(Clock);
        Catalog = catalog ?? BuiltInRuleCatalog.Default;
        Seed = CreateSeedService(Catalog);
        Service = new BlockRuleService(Rules, Session, Audits, Correlation, Logger, Clock);
    }

    public FakeBlockRuleRepository Rules { get; } = new();

    public RecordingAuditLogRepository Audits { get; } = new();

    public RecordingLogger Logger { get; } = new();

    public TestCorrelationContext Correlation { get; }

    public ManualTimeProvider Clock { get; }

    public ParentSessionState Session { get; }

    public BuiltInRuleCatalog Catalog { get; }

    public BlockRuleSeedService Seed { get; }

    public BlockRuleService Service { get; }

    public void Authenticate() => Session.Authenticate();

    public BlockRuleSeedService CreateSeedService(BuiltInRuleCatalog catalog) =>
        new(Rules, Audits, Correlation, Logger, Clock, catalog);
}
