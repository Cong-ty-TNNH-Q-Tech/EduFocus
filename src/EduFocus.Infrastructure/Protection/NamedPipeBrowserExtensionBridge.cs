using System.Buffers.Binary;
using System.Collections.Concurrent;
using System.IO.Pipes;
using System.Security.AccessControl;
using System.Security.Principal;
using System.Text.Json;
using EduFocus.Domain.Entities;
using EduFocus.Domain.Enums;
using EduFocus.Domain.Privacy;
using EduFocus.Domain.Protection;
using EduFocus.Ports.Outbound.Services;

namespace EduFocus.Infrastructure.Protection;

/// <summary>
/// Serves the effective rule snapshot to the browser extension over a local
/// named pipe and accepts verified blocked-attempt reports. The extension only
/// reports matched URLs, so normal browsing never leaves the browser. The pipe
/// exists only while protection is active; its absence makes the extension
/// fail open (issue #45).
/// </summary>
public sealed class NamedPipeBrowserExtensionBridge : IBrowserExtensionBridge, IAsyncDisposable
{
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);

    private readonly BrowserExtensionBridgeOptions _options;
    private readonly IAppLogger _logger;
    private readonly TimeProvider _timeProvider;
    private readonly SemaphoreSlim _stateGate = new(1, 1);
    private readonly ConcurrentDictionary<Guid, ClientConnection> _clients = new();
    private readonly object _snapshotLock = new();

    private CancellationTokenSource? _cts;
    private Task? _acceptLoop;
    private BlockRuleSet _ruleSet = BlockRuleSet.Create([]);
    private byte[] _snapshot;
    private volatile bool _running;

    public NamedPipeBrowserExtensionBridge(
        BrowserExtensionBridgeOptions options,
        IAppLogger logger,
        TimeProvider timeProvider)
    {
        _options = options ?? throw new ArgumentNullException(nameof(options));
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
        _timeProvider = timeProvider ?? throw new ArgumentNullException(nameof(timeProvider));
        _snapshot = SerializeSnapshot([], _timeProvider.GetUtcNow());

        if (string.IsNullOrWhiteSpace(_options.PipeName))
            throw new ArgumentException("Pipe name is required.", nameof(options));

        if (_options.MaxClients is < 1 or > 254)
            throw new ArgumentOutOfRangeException(nameof(options), "MaxClients must be between 1 and 254.");
    }

    public bool IsRunning => _running;

    public event EventHandler<BrowserBlockedAttempt>? BlockedAttemptObserved;

    public async Task StartAsync(
        IReadOnlyList<BlockRule> rules,
        DateTime nowUtc,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(rules);

        await _stateGate.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            if (_running)
            {
                UpdateState(rules, nowUtc);
                await BroadcastAsync(cancellationToken).ConfigureAwait(false);
                return;
            }

            var listener = CreateListener();
            var cts = new CancellationTokenSource();

            UpdateState(rules, nowUtc);
            _cts = cts;
            _running = true;
            _acceptLoop = AcceptLoopAsync(listener, cts.Token);

            _logger.Info(LogCategories.Protection, "Browser extension bridge started.");
        }
        finally
        {
            _stateGate.Release();
        }
    }

    public async Task UpdateRulesAsync(
        IReadOnlyList<BlockRule> rules,
        DateTime nowUtc,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(rules);

        await _stateGate.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            if (!_running)
                return;

            UpdateState(rules, nowUtc);
            await BroadcastAsync(cancellationToken).ConfigureAwait(false);
        }
        finally
        {
            _stateGate.Release();
        }
    }

    public async Task StopAsync(CancellationToken cancellationToken = default)
    {
        Task? acceptLoop;

        await _stateGate.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            if (!_running)
                return;

            _running = false;
            _cts?.Cancel();
            acceptLoop = _acceptLoop;
            _acceptLoop = null;

            foreach (var client in _clients.Values)
                client.Dispose();

            _clients.Clear();
            _cts?.Dispose();
            _cts = null;

            _logger.Info(LogCategories.Protection, "Browser extension bridge stopped.");
        }
        finally
        {
            _stateGate.Release();
        }

        if (acceptLoop is not null)
        {
            try
            {
                await acceptLoop.WaitAsync(TimeSpan.FromSeconds(2), CancellationToken.None)
                    .ConfigureAwait(false);
            }
            catch (TimeoutException)
            {
                _logger.Warning(LogCategories.Protection, "Browser extension bridge did not stop in time.");
            }
            catch (Exception ex)
            {
                _logger.Warning(
                    LogCategories.Protection,
                    $"Browser extension bridge stopped with an error ({ex.GetType().Name}).");
            }
        }
    }

    public async ValueTask DisposeAsync()
    {
        await StopAsync(CancellationToken.None).ConfigureAwait(false);
        _stateGate.Dispose();
    }

    private async Task AcceptLoopAsync(NamedPipeServerStream listener, CancellationToken cancellationToken)
    {
        // Ownership of the connected instance moves to ClientConnection; only
        // dispose a listener that is still waiting for a connection.
        NamedPipeServerStream? owned = listener;
        try
        {
            while (!cancellationToken.IsCancellationRequested)
            {
                await owned.WaitForConnectionAsync(cancellationToken).ConfigureAwait(false);

                NamedPipeServerStream? next = null;
                try
                {
                    next = CreateListener();
                }
                catch (Exception ex)
                {
                    _logger.Error(
                        LogCategories.Protection,
                        "Could not create another browser bridge listener.",
                        ex);
                }

                var client = new ClientConnection(Guid.NewGuid(), owned);
                _clients[client.Id] = client;
                _ = HandleClientAsync(client, cancellationToken);

                owned = next;
                if (owned is null)
                    return;
            }
        }
        catch (OperationCanceledException)
        {
        }
        catch (Exception ex)
        {
            _logger.Error(
                LogCategories.Protection,
                "Browser extension bridge listener failed.",
                ex);
        }
        finally
        {
            owned?.Dispose();
        }
    }

    private async Task HandleClientAsync(ClientConnection client, CancellationToken cancellationToken)
    {
        try
        {
            await _stateGate.WaitAsync(cancellationToken).ConfigureAwait(false);
            try
            {
                await client.WriteAsync(GetSnapshot(), cancellationToken).ConfigureAwait(false);
                client.IsReady = true;
            }
            finally
            {
                _stateGate.Release();
            }

            while (!cancellationToken.IsCancellationRequested)
            {
                var payload = await client.ReadAsync(cancellationToken).ConfigureAwait(false);
                if (payload is null)
                    break;

                HandleMessage(payload);
            }
        }
        catch (OperationCanceledException)
        {
        }
        catch (IOException)
        {
        }
        catch (ObjectDisposedException)
        {
        }
        catch (Exception ex)
        {
            _logger.Warning(
                LogCategories.Protection,
                $"Browser extension bridge client failed ({ex.GetType().Name}).");
        }
        finally
        {
            _clients.TryRemove(client.Id, out _);
            client.Dispose();
        }
    }

    private void HandleMessage(byte[] payload)
    {
        try
        {
            using var document = JsonDocument.Parse(payload);
            var root = document.RootElement;

            if (!root.TryGetProperty("type", out var type)
                || type.GetString() != "blocked"
                || !root.TryGetProperty("url", out var urlElement))
            {
                return;
            }

            var sanitized = UrlSanitizer.Sanitize(urlElement.GetString());
            if (sanitized is null)
                return;

            var now = _timeProvider.GetUtcNow();
            if (Volatile.Read(ref _ruleSet).EvaluateUrl(sanitized, now.UtcDateTime) != RuleDecision.Block)
                return;

            try
            {
                BlockedAttemptObserved?.Invoke(this, new BrowserBlockedAttempt(sanitized, now));
            }
            catch (Exception ex)
            {
                _logger.Error(
                    LogCategories.Protection,
                    "A browser attempt subscriber failed.",
                    ex);
            }
        }
        catch (JsonException)
        {
        }
    }

    private void UpdateState(IReadOnlyList<BlockRule> rules, DateTime nowUtc)
    {
        var effective = rules
            .Where(rule => rule.RuleType is RuleType.Domain or RuleType.Url)
            .Where(rule => rule.IsEffective(nowUtc))
            .ToArray();

        var snapshot = SerializeSnapshot(effective, new DateTimeOffset(nowUtc, TimeSpan.Zero));

        lock (_snapshotLock)
        {
            _ruleSet = BlockRuleSet.Create(effective);
            _snapshot = snapshot;
        }
    }

    private byte[] GetSnapshot()
    {
        lock (_snapshotLock)
        {
            return _snapshot;
        }
    }

    private async Task BroadcastAsync(CancellationToken cancellationToken)
    {
        var snapshot = GetSnapshot();
        foreach (var client in _clients.Values.Where(client => client.IsReady).ToArray())
        {
            try
            {
                await client.WriteAsync(snapshot, cancellationToken).ConfigureAwait(false);
            }
            catch (OperationCanceledException)
            {
                throw;
            }
            catch (Exception)
            {
                _clients.TryRemove(client.Id, out _);
                client.Dispose();
            }
        }
    }

    private static byte[] SerializeSnapshot(
        IReadOnlyList<BlockRule> rules,
        DateTimeOffset generatedAtUtc)
    {
        var message = new SnapshotMessage(
            "snapshot",
            1,
            generatedAtUtc,
            rules.Select(ToSnapshotRule).ToArray(),
            ProtectedDomains.SharedApexDomains.OrderBy(value => value, StringComparer.Ordinal).ToArray(),
            ProtectedDomains.ServiceDomains.OrderBy(value => value, StringComparer.Ordinal).ToArray());

        return JsonSerializer.SerializeToUtf8Bytes(message, JsonOptions);
    }

    private static SnapshotRule ToSnapshotRule(BlockRule rule) =>
        new(
            rule.Id.Value.ToString("D"),
            rule.RuleType == RuleType.Url ? "url" : "domain",
            rule.MatchType.ToString().ToLowerInvariant(),
            rule.NormalizedValue,
            rule.Effect == BlockRuleEffect.Allow ? "allow" : "block",
            rule.Category.ToString().ToLowerInvariant(),
            rule.ExpiresAtUtc);

    private NamedPipeServerStream CreateListener()
    {
        var options = PipeOptions.Asynchronous | PipeOptions.WriteThrough;
        var bufferSize = BrowserBridgeProtocol.MaxMessageBytes;

        if (!OperatingSystem.IsWindows())
        {
            return new NamedPipeServerStream(
                _options.PipeName,
                PipeDirection.InOut,
                _options.MaxClients,
                PipeTransmissionMode.Byte,
                options,
                bufferSize,
                bufferSize);
        }

        var security = new PipeSecurity();

        // The server identity needs CreateNewInstance to open additional pipe
        // instances; other authenticated users may only connect as clients.
        if (WindowsIdentity.GetCurrent().User is { } currentUser)
        {
            security.AddAccessRule(
                new PipeAccessRule(currentUser, PipeAccessRights.FullControl, AccessControlType.Allow));
        }

        security.AddAccessRule(
            new PipeAccessRule(
                new SecurityIdentifier(WellKnownSidType.AuthenticatedUserSid, null),
                PipeAccessRights.ReadWrite,
                AccessControlType.Allow));

        return NamedPipeServerStreamAcl.Create(
            _options.PipeName,
            PipeDirection.InOut,
            _options.MaxClients,
            PipeTransmissionMode.Byte,
            options,
            bufferSize,
            bufferSize,
            security);
    }

    private sealed record SnapshotMessage(
        string Type,
        int Protocol,
        DateTimeOffset GeneratedAtUtc,
        IReadOnlyList<SnapshotRule> Rules,
        IReadOnlyList<string> ProtectedExact,
        IReadOnlyList<string> ProtectedServices);

    private sealed record SnapshotRule(
        string Id,
        string Type,
        string Match,
        string Value,
        string Effect,
        string Category,
        DateTime? ExpiresAtUtc);

    private sealed class ClientConnection : IDisposable
    {
        private readonly NamedPipeServerStream _stream;
        private readonly SemaphoreSlim _writeGate = new(1, 1);

        public ClientConnection(Guid id, NamedPipeServerStream stream)
        {
            Id = id;
            _stream = stream;
        }

        public Guid Id { get; }

        public bool IsReady { get; set; }

        public async Task WriteAsync(byte[] payload, CancellationToken cancellationToken)
        {
            var header = new byte[4];
            BinaryPrimitives.WriteUInt32LittleEndian(header, (uint)payload.Length);

            await _writeGate.WaitAsync(cancellationToken).ConfigureAwait(false);
            try
            {
                await _stream.WriteAsync(header, cancellationToken).ConfigureAwait(false);
                await _stream.WriteAsync(payload, cancellationToken).ConfigureAwait(false);
                await _stream.FlushAsync(cancellationToken).ConfigureAwait(false);
            }
            finally
            {
                _writeGate.Release();
            }
        }

        public async Task<byte[]?> ReadAsync(CancellationToken cancellationToken)
        {
            var header = new byte[4];
            if (!await FillAsync(header, cancellationToken).ConfigureAwait(false))
                return null;

            var length = BinaryPrimitives.ReadUInt32LittleEndian(header);
            if (length == 0 || length > BrowserBridgeProtocol.MaxIncomingMessageBytes)
                throw new InvalidDataException("Browser bridge message is too large.");

            var payload = new byte[length];
            return await FillAsync(payload, cancellationToken).ConfigureAwait(false) ? payload : null;
        }

        public void Dispose()
        {
            _stream.Dispose();
            _writeGate.Dispose();
        }

        private async Task<bool> FillAsync(byte[] buffer, CancellationToken cancellationToken)
        {
            var read = 0;
            while (read < buffer.Length)
            {
                var count = await _stream.ReadAsync(buffer.AsMemory(read), cancellationToken)
                    .ConfigureAwait(false);
                if (count == 0)
                    return false;

                read += count;
            }

            return true;
        }
    }
}
