using System.Collections.Concurrent;
using System.Net.WebSockets;
using System.Text.Json;

namespace EduFocus.Cloud.Api.Push;

public sealed record PushStreamSendResult(Guid DeviceId, bool Success);

public sealed class PushStreamConnection
{
    private readonly SemaphoreSlim _sendLock = new(1, 1);

    public PushStreamConnection(Guid deviceId, Guid familyId, WebSocket socket)
    {
        DeviceId = deviceId;
        FamilyId = familyId;
        Socket = socket;
    }

    public Guid DeviceId { get; }

    public Guid FamilyId { get; }

    public WebSocket Socket { get; }

    public async Task<bool> TrySendAsync(
        ReadOnlyMemory<byte> payload,
        CancellationToken cancellationToken)
    {
        if (Socket.State != WebSocketState.Open)
        {
            return false;
        }

        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        timeout.CancelAfter(TimeSpan.FromSeconds(10));
        var acquired = false;
        try
        {
            await _sendLock.WaitAsync(timeout.Token);
            acquired = true;
            if (Socket.State != WebSocketState.Open)
            {
                return false;
            }

            await Socket.SendAsync(
                payload,
                WebSocketMessageType.Text,
                endOfMessage: true,
                timeout.Token);
            return true;
        }
        catch (OperationCanceledException)
        {
            return false;
        }
        catch (Exception exception) when (
            exception is WebSocketException or ObjectDisposedException or InvalidOperationException)
        {
            return false;
        }
        finally
        {
            if (acquired)
            {
                _sendLock.Release();
            }
        }
    }

    public void Abort()
    {
        try
        {
            Socket.Abort();
        }
        catch
        {
        }
    }

    public async Task CloseAsync()
    {
        var acquired = false;
        try
        {
            if (Socket.State is WebSocketState.Open or WebSocketState.CloseReceived)
            {
                using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(2));
                await _sendLock.WaitAsync(timeout.Token);
                acquired = true;
                if (Socket.State is WebSocketState.Open or WebSocketState.CloseReceived)
                {
                    await Socket.CloseOutputAsync(
                        WebSocketCloseStatus.NormalClosure,
                        "closed",
                        timeout.Token);
                }
            }
        }
        catch
        {
        }
        finally
        {
            if (acquired)
            {
                _sendLock.Release();
            }
        }
    }
}

public sealed class PushStreamRegistry(ILogger<PushStreamRegistry> logger)
{
    private readonly ConcurrentDictionary<Guid, PushStreamConnection> _connections = new();

    public int Count => _connections.Count;

    public PushStreamConnection Register(Guid deviceId, Guid familyId, WebSocket socket)
    {
        var connection = new PushStreamConnection(deviceId, familyId, socket);
        while (true)
        {
            if (_connections.TryGetValue(deviceId, out var existing))
            {
                if (!_connections.TryUpdate(deviceId, connection, existing))
                {
                    continue;
                }

                existing.Abort();
                return connection;
            }

            if (_connections.TryAdd(deviceId, connection))
            {
                return connection;
            }
        }
    }

    public void Unregister(PushStreamConnection connection)
    {
        var collection = (ICollection<KeyValuePair<Guid, PushStreamConnection>>)_connections;
        collection.Remove(new KeyValuePair<Guid, PushStreamConnection>(
            connection.DeviceId,
            connection));
    }

    public void Disconnect(Guid deviceId)
    {
        if (_connections.TryRemove(deviceId, out var connection))
        {
            connection.Abort();
            logger.LogInformation(
                "Push stream connection for device {DeviceId} was closed.",
                deviceId);
        }
    }

    public void DisconnectFamily(Guid familyId)
    {
        foreach (var connection in GetConnections(familyId))
        {
            Disconnect(connection.DeviceId);
        }
    }

    public IReadOnlyList<PushStreamConnection> GetConnections(Guid familyId) =>
        _connections.Values
            .Where(connection => connection.FamilyId == familyId)
            .ToList();

    public async Task<IReadOnlyList<PushStreamSendResult>> SendAsync(
        Guid familyId,
        IReadOnlyDictionary<string, string> data,
        CancellationToken cancellationToken)
    {
        var connections = GetConnections(familyId);
        if (connections.Count == 0)
        {
            return [];
        }

        var payload = JsonSerializer.SerializeToUtf8Bytes(data);
        var tasks = connections
            .Select(async connection =>
            {
                var success = await connection.TrySendAsync(payload, cancellationToken);
                if (!success)
                {
                    Unregister(connection);
                    connection.Abort();
                    logger.LogInformation(
                        "Push stream connection for device {DeviceId} was removed.",
                        connection.DeviceId);
                }

                return new PushStreamSendResult(connection.DeviceId, success);
            })
            .ToArray();

        return await Task.WhenAll(tasks);
    }
}
