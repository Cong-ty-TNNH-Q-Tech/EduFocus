namespace EduFocus.Service.Runtime;

/// <summary>
/// Process-wide start marker shared by the runtime and the command handler so
/// the UI handshake reports the real service start time.
/// </summary>
public sealed class ServiceRuntimeStatus
{
    private long _startedAtUtcTicks;

    public DateTimeOffset? StartedAtUtc
    {
        get
        {
            var ticks = Interlocked.Read(ref _startedAtUtcTicks);
            return ticks == 0 ? null : new DateTimeOffset(ticks, TimeSpan.Zero);
        }
    }

    public void MarkStarted(DateTimeOffset startedAtUtc) =>
        Interlocked.Exchange(ref _startedAtUtcTicks, startedAtUtc.UtcTicks);
}
