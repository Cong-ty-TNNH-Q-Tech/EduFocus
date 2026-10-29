using EduFocus.Domain.Entities;

namespace EduFocus.Ports.Outbound.Services;

/// <summary>
/// Local bridge between EduFocus and the browser extension native host. The
/// bridge publishes an effective rule snapshot while protection is active and
/// receives blocked-attempt reports. It must fail open: when it is not running
/// the extension allows every navigation (issue #45).
/// </summary>
public interface IBrowserExtensionBridge
{
    bool IsRunning { get; }

    Task StartAsync(
        IReadOnlyList<BlockRule> rules,
        DateTime nowUtc,
        CancellationToken cancellationToken = default);

    Task UpdateRulesAsync(
        IReadOnlyList<BlockRule> rules,
        DateTime nowUtc,
        CancellationToken cancellationToken = default);

    Task StopAsync(CancellationToken cancellationToken = default);

    /// <summary>Raised only after the reported URL was verified against the active rules.</summary>
    event EventHandler<BrowserBlockedAttempt>? BlockedAttemptObserved;
}

public sealed record BrowserBlockedAttempt(string Url, DateTimeOffset ObservedAtUtc);

/// <summary>Shared constants for the pipe and native-messaging framing.</summary>
public static class BrowserBridgeProtocol
{
    public const string PipeName = "EduFocus.BrowserBridge.v1";

    public const int MaxIncomingMessageBytes = 16 * 1024;

    public const int MaxMessageBytes = 1024 * 1024;
}
