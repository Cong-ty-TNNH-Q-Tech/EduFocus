namespace EduFocus.Infrastructure.Protection;

/// <summary>
/// Polling and close behavior for the app blocker. The default cadence keeps
/// scans cheap; tests use millisecond values.
/// </summary>
public sealed record ProcessWatcherOptions
{
    public TimeSpan PollInterval { get; init; } = TimeSpan.FromSeconds(2);

    public TimeSpan CloseTimeout { get; init; } = TimeSpan.FromSeconds(2);

    public TimeSpan Debounce { get; init; } = TimeSpan.FromSeconds(30);
}
