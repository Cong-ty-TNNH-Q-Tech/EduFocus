namespace EduFocus.Domain.Entities;

/// <summary>In-memory authorization window. It is intentionally never persisted across app restarts.</summary>
public sealed class ParentModeSession
{
    public static readonly TimeSpan IdleTimeout = TimeSpan.FromMinutes(5);
    public DateTimeOffset? ExpiresAtUtc { get; private set; }
    public bool IsActive(DateTimeOffset now)
    {
        now = RequireUtc(now);
        if (ExpiresAtUtc is not { } expiry || now >= expiry)
        {
            ExpiresAtUtc = null;
            return false;
        }
        return true;
    }
    public void Unlock(DateTimeOffset now) => ExpiresAtUtc = RequireUtc(now).Add(IdleTimeout);
    public bool RecordActivity(DateTimeOffset now)
    {
        now = RequireUtc(now);
        if (!IsActive(now)) return false;
        ExpiresAtUtc = now.Add(IdleTimeout);
        return true;
    }
    public void Lock() => ExpiresAtUtc = null;
    public TimeSpan Remaining(DateTimeOffset now) => IsActive(now) ? ExpiresAtUtc!.Value - now : TimeSpan.Zero;

    private static DateTimeOffset RequireUtc(DateTimeOffset now) => now.Offset == TimeSpan.Zero ? now : throw new ArgumentException("Timestamp must be UTC.", nameof(now));
}
