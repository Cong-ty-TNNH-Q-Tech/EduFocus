namespace EduFocus.Ports.Outbound.Services;

public enum ProcessBlockOutcome
{
    ClosedGracefully,
    Killed,
    Failed
}

/// <summary>
/// An app block observed by the process watcher. The engine debounces and
/// records these as BlockedAttempt rows (TargetType = App,
/// ObservedVia = ProcessWatcher); no window title, path or document content is
/// captured.
/// </summary>
public sealed record ProcessBlockedObservation(
    DateTimeOffset ObservedAtUtc,
    string ProcessName,
    int ProcessId,
    ProcessBlockOutcome Outcome,
    string? Warning);

/// <summary>
/// Watches for blocked applications while Protection is ON (issue #23).
/// Implementations only act on exact, normalized process names, skip system
/// and EduFocus processes and never throw from the polling loop.
/// </summary>
public interface IProcessWatcher
{
    bool IsRunning { get; }

    Task StartAsync(
        IReadOnlyCollection<string> blockedProcessNames,
        IReadOnlyCollection<string> allowedProcessNames,
        CancellationToken cancellationToken = default);

    void UpdateRules(
        IReadOnlyCollection<string> blockedProcessNames,
        IReadOnlyCollection<string> allowedProcessNames);

    Task StopAsync(CancellationToken cancellationToken = default);

    event Action<ProcessBlockedObservation>? ProcessBlockedObserved;
}
