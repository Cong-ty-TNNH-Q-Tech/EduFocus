using System.Text.Json;
using EduFocus.Ports.DTOs;
using EduFocus.Ports.Outbound.Services;

namespace EduFocus.Infrastructure.Watchdog;

/// <summary>
/// Heartbeat file written by the app every few seconds. A torn or missing file
/// is treated as "no heartbeat"; the watchdog then falls back to the process
/// check instead of restarting the app on a transient read error.
/// </summary>
public sealed class JsonWatchdogHeartbeatStore : IWatchdogHeartbeatStore
{
    private readonly WatchdogRuntimeFiles _files;
    private readonly IAppLogger _logger;

    public JsonWatchdogHeartbeatStore(WatchdogRuntimeFiles files, IAppLogger logger)
    {
        _files = files ?? throw new ArgumentNullException(nameof(files));
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
    }

    public Task<WatchdogHeartbeat?> ReadAsync(CancellationToken cancellationToken = default)
    {
        var text = _files.TryReadAllText(_files.HeartbeatPath);
        if (text is null)
            return Task.FromResult<WatchdogHeartbeat?>(null);

        try
        {
            return Task.FromResult(JsonSerializer.Deserialize<WatchdogHeartbeat>(text, WatchdogJson.Options));
        }
        catch (JsonException ex)
        {
            _logger.Warning(LogCategories.Watchdog, $"Ignoring an unreadable app heartbeat: {ex.Message}");
            return Task.FromResult<WatchdogHeartbeat?>(null);
        }
    }

    public Task WriteAsync(WatchdogHeartbeat heartbeat, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(heartbeat);
        _files.WriteAtomic(_files.HeartbeatPath, JsonSerializer.Serialize(heartbeat, WatchdogJson.Options));
        return Task.CompletedTask;
    }

    public Task DeleteAsync(CancellationToken cancellationToken = default)
    {
        _files.TryDelete(_files.HeartbeatPath);
        return Task.CompletedTask;
    }
}
