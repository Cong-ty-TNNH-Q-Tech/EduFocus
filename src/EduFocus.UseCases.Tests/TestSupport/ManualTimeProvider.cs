namespace EduFocus.UseCases.Tests.TestSupport;

internal sealed class ManualTimeProvider : TimeProvider
{
    private readonly TimeZoneInfo? _localTimeZone;
    private DateTimeOffset _now;

    public ManualTimeProvider(DateTimeOffset now, TimeZoneInfo? localTimeZone = null)
    {
        _now = now;
        _localTimeZone = localTimeZone;
    }

    public override TimeZoneInfo LocalTimeZone => _localTimeZone ?? base.LocalTimeZone;

    public override DateTimeOffset GetUtcNow() => _now;

    public void Advance(TimeSpan delta) => _now += delta;
}
