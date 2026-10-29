namespace EduFocus.Infrastructure.Protection;

/// <summary>
/// Central blocked-attempt debounce. The process watcher has its own debounce
/// to avoid kill loops; the engine debounce also covers block-page hits and is
/// the one that governs database writes.
/// </summary>
public sealed record ProtectionEngineOptions
{
    public TimeSpan Debounce { get; init; } = TimeSpan.FromSeconds(30);
}
