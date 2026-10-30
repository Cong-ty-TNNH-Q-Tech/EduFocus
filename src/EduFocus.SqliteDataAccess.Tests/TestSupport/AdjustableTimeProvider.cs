namespace EduFocus.SqliteDataAccess.Tests.TestSupport;

internal sealed class AdjustableTimeProvider : TimeProvider
{
    private DateTimeOffset _utcNow;

    public AdjustableTimeProvider(DateTime utcNow)
    {
        if (utcNow.Kind != DateTimeKind.Utc)
            throw new ArgumentException("The start time must be UTC.", nameof(utcNow));

        _utcNow = new DateTimeOffset(utcNow);
    }

    public override DateTimeOffset GetUtcNow() => _utcNow;

    public void Advance(TimeSpan delta) => _utcNow += delta;
}
