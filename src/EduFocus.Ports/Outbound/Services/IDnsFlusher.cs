namespace EduFocus.Ports.Outbound.Services;

/// <summary>
/// Flushes the operating-system DNS cache after a hosts change. Implementations
/// are best-effort: the caller logs a warning when flushing fails because the
/// hosts entries still take effect after the cache expires.
/// </summary>
public interface IDnsFlusher
{
    Task FlushAsync(CancellationToken cancellationToken = default);
}
