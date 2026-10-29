using System.Text.Json;
using EduFocus.Ports.DTOs;
using EduFocus.Ports.Outbound.Services;

namespace EduFocus.Infrastructure.Watchdog;

/// <summary>
/// Durable restart budget. Written only by the watchdog, so killing and
/// relaunching the watchdog does not reset the crash-loop protection.
/// </summary>
public sealed class JsonWatchdogStateStore : IWatchdogStateStore
{
    private readonly WatchdogRuntimeFiles _files;
    private readonly IAppLogger _logger;

    public JsonWatchdogStateStore(WatchdogRuntimeFiles files, IAppLogger logger)
    {
        _files = files ?? throw new ArgumentNullException(nameof(files));
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
    }

    public Task<WatchdogRestartState?> ReadAsync(CancellationToken cancellationToken = default)
    {
        var text = _files.TryReadAllText(_files.StatePath);
        if (text is null)
            return Task.FromResult<WatchdogRestartState?>(null);

        try
        {
            return Task.FromResult(JsonSerializer.Deserialize<WatchdogRestartState>(text, WatchdogJson.Options));
        }
        catch (JsonException ex)
        {
            _logger.Warning(LogCategories.Watchdog, $"Ignoring unreadable watchdog state: {ex.Message}");
            return Task.FromResult<WatchdogRestartState?>(null);
        }
    }

    public Task WriteAsync(WatchdogRestartState state, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(state);
        _files.WriteAtomic(_files.StatePath, JsonSerializer.Serialize(state, WatchdogJson.Options));
        return Task.CompletedTask;
    }

    public Task DeleteAsync(CancellationToken cancellationToken = default)
    {
        _files.TryDelete(_files.StatePath);
        return Task.CompletedTask;
    }
}
