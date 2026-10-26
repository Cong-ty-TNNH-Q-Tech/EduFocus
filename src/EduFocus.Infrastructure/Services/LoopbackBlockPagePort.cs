using System.Net;
using System.Net.Sockets;
using System.Text;
using EduFocus.Domain.Entities;
using EduFocus.Domain.Enums;
using EduFocus.Ports.Outbound.Services;
using Microsoft.Extensions.DependencyInjection;

namespace EduFocus.Infrastructure.Services;

/// <summary>Serves a static, dependency-free block page only to local HTTP clients.</summary>
public sealed class LoopbackBlockPagePort : IBlockPagePort, IAsyncDisposable
{
    private const string DefaultPrefix = "http://127.0.0.1:80/";
    private const int MaximumHeaderBytes = 8192;
    private static readonly SemaphoreSlim LifecycleGate = new(1, 1);
    private readonly int _port;
    private readonly IAppLogger _logger;
    private readonly IServiceScopeFactory? _scopeFactory;
    private IReadOnlySet<string> _blockedDomains = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
    private TcpListener? _listener;
    private CancellationTokenSource? _stop;
    private Task? _serveTask;
    private int _serving;

    public LoopbackBlockPagePort(IAppLogger logger, string prefix = DefaultPrefix, IServiceScopeFactory? scopeFactory = null)
    {
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
        _scopeFactory = scopeFactory;
        ArgumentNullException.ThrowIfNull(prefix);
        if (!Uri.TryCreate(prefix, UriKind.Absolute, out var uri) || uri.Scheme != Uri.UriSchemeHttp ||
            !IPAddress.TryParse(uri.Host, out var address) || !IPAddress.IsLoopback(address) || uri.AbsolutePath != "/" || uri.Port is < 1 or > 65535)
            throw new ArgumentException("Block page listener must use an HTTP loopback root URL.", nameof(prefix));
        _port = uri.Port;
    }

    public bool IsAvailable => Volatile.Read(ref _serving) == 1 && _listener is not null;

    public async Task<bool> StartAsync(IReadOnlyCollection<BlockRule> rules, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(rules);
        await LifecycleGate.WaitAsync(cancellationToken);
        try
        {
            _blockedDomains = GetBlockedDomains(rules, DateTimeOffset.UtcNow);
            if (IsAvailable) return true;
            if (_listener is not null)
            {
                _stop?.Cancel();
                _listener.Stop();
                if (_serveTask is not null)
                {
                    try { await _serveTask; } catch { }
                }
                _stop?.Dispose();
                _listener = null;
                _stop = null;
                _serveTask = null;
            }
            var listener = new TcpListener(IPAddress.Loopback, _port);
            try { listener.Start(backlog: 16); }
            catch (Exception ex) when (ex is SocketException or InvalidOperationException or UnauthorizedAccessException)
            {
                listener.Stop();
                _logger.Warning("EduFocus block page listener could not bind to loopback HTTP; Protection can continue without the explanatory page.");
                return false;
            }
            _listener = listener;
            _stop = new CancellationTokenSource();
            Volatile.Write(ref _serving, 1);
            _serveTask = ServeAsync(listener, _stop.Token);
            return true;
        }
        finally { LifecycleGate.Release(); }
    }

    public Task UpdateRulesAsync(IReadOnlyCollection<BlockRule> rules, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(rules);
        cancellationToken.ThrowIfCancellationRequested();
        _blockedDomains = GetBlockedDomains(rules, DateTimeOffset.UtcNow);
        return Task.CompletedTask;
    }

    public async Task StopAsync(CancellationToken cancellationToken = default)
    {
        await LifecycleGate.WaitAsync(cancellationToken);
        try
        {
            var listener = _listener;
            var stop = _stop;
            var serveTask = _serveTask;
            _listener = null;
            _stop = null;
            _serveTask = null;
            _blockedDomains = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            Volatile.Write(ref _serving, 0);
            if (stop is not null) stop.Cancel();
            listener?.Stop();
            if (serveTask is not null)
            {
                try { await serveTask.WaitAsync(cancellationToken); }
                catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested) { throw; }
                catch (Exception ex) { _logger.Warning($"EduFocus block page listener stopped after a request error: {ex.GetType().Name}."); }
            }
            stop?.Dispose();
        }
        finally { LifecycleGate.Release(); }
    }

    public async ValueTask DisposeAsync() => await StopAsync();

    public static string BuildPage(string? untrustedHost)
    {
        var host = string.IsNullOrWhiteSpace(untrustedHost) ? "the requested website" : WebUtility.HtmlEncode(untrustedHost);
        return "<!doctype html><html lang=\"en\"><head><meta charset=\"utf-8\"><meta name=\"viewport\" content=\"width=device-width,initial-scale=1\"><title>Website blocked</title><style>body{font:16px system-ui,sans-serif;background:#f5f7fa;color:#17324d;margin:0;display:grid;min-height:100vh;place-items:center}.card{max-width:36rem;margin:1rem;padding:2rem;background:white;border-radius:1rem;box-shadow:0 8px 28px #17324d20}h1{font-size:1.6rem}p{line-height:1.6;color:#52677a}</style></head><body><main class=\"card\"><h1>Website blocked</h1><p><strong>" + host + "</strong> is blocked while EduFocus protection is active.</p><p>If you need this website for school, ask your parent to review the protection settings.</p></main></body></html>";
    }

    private async Task ServeAsync(TcpListener listener, CancellationToken cancellationToken)
    {
        try
        {
            while (!cancellationToken.IsCancellationRequested)
            {
                using var client = await listener.AcceptTcpClientAsync(cancellationToken);
                await RespondAsync(client, cancellationToken);
            }
        }
        catch (Exception ex) when (cancellationToken.IsCancellationRequested && (ex is OperationCanceledException or SocketException or ObjectDisposedException)) { }
        catch (Exception ex)
        {
            _logger.Error("EduFocus block page listener stopped unexpectedly; Protection state is unchanged.", ex);
        }
        finally { Volatile.Write(ref _serving, 0); }
    }

    private async Task RespondAsync(TcpClient client, CancellationToken cancellationToken)
    {
        var stream = client.GetStream();
        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        timeout.CancelAfter(TimeSpan.FromSeconds(2));
        var headerBytes = new List<byte>(512);
        var one = new byte[1];
        try
        {
            while (headerBytes.Count < MaximumHeaderBytes)
            {
                var read = await stream.ReadAsync(one, timeout.Token);
                if (read == 0) return;
                headerBytes.Add(one[0]);
                var count = headerBytes.Count;
                if (count >= 4 && headerBytes[count - 4] == 13 && headerBytes[count - 3] == 10 && headerBytes[count - 2] == 13 && headerBytes[count - 1] == 10)
                    break;
            }
            if (headerBytes.Count >= MaximumHeaderBytes)
            {
                await WriteResponseAsync(stream, "413 Payload Too Large", "text/plain; charset=utf-8", "Request headers are too large.", timeout.Token);
                return;
            }

            var header = Encoding.ASCII.GetString(headerBytes.ToArray());
            var lines = header.Split("\r\n", StringSplitOptions.None);
            var requestLine = lines.FirstOrDefault()?.Split(' ', StringSplitOptions.RemoveEmptyEntries) ?? [];
            if (requestLine.Length != 3 || requestLine[0] != "GET")
            {
                await WriteResponseAsync(stream, "405 Method Not Allowed", "text/plain; charset=utf-8", "Only GET is supported.", timeout.Token);
                return;
            }
            var hostHeader = lines.Skip(1).Select(line => line.Split(':', 2))
                .FirstOrDefault(parts => parts.Length == 2 && parts[0].Equals("Host", StringComparison.OrdinalIgnoreCase))?[1].Trim();
            var host = ExtractDomain(hostHeader);
            if (host is null || !_blockedDomains.Contains(host))
            {
                await WriteResponseAsync(stream, "404 Not Found", "text/plain; charset=utf-8", "Not found.", timeout.Token);
                return;
            }
            await RecordBlockedDomainAsync(host, cancellationToken);
            var body = Encoding.UTF8.GetBytes(BuildPage(host));
            await WriteResponseAsync(stream, "200 OK", "text/html; charset=utf-8", body, timeout.Token);
        }
        catch (Exception ex) when (ex is IOException or SocketException or OperationCanceledException)
        {
            // A browser closing its connection while navigating is normal; never log its URL or query.
        }
    }

    private static Task WriteResponseAsync(NetworkStream stream, string status, string contentType, string body, CancellationToken cancellationToken) =>
        WriteResponseAsync(stream, status, contentType, Encoding.UTF8.GetBytes(body), cancellationToken);

    private static async Task WriteResponseAsync(NetworkStream stream, string status, string contentType, byte[] body, CancellationToken cancellationToken)
    {
        var header = Encoding.ASCII.GetBytes($"HTTP/1.1 {status}\r\nContent-Type: {contentType}\r\nContent-Length: {body.Length}\r\nCache-Control: no-store, max-age=0\r\nPragma: no-cache\r\nContent-Security-Policy: default-src 'none'; style-src 'unsafe-inline'; img-src 'none'; connect-src 'none'; form-action 'none'; base-uri 'none'; frame-ancestors 'none'\r\nX-Content-Type-Options: nosniff\r\nX-Frame-Options: DENY\r\nReferrer-Policy: no-referrer\r\nConnection: close\r\n\r\n");
        await stream.WriteAsync(header, cancellationToken);
        await stream.WriteAsync(body, cancellationToken);
        await stream.FlushAsync(cancellationToken);
    }

    private static string? ExtractDomain(string? hostHeader)
    {
        if (string.IsNullOrWhiteSpace(hostHeader) || !Uri.TryCreate("http://" + hostHeader, UriKind.Absolute, out var uri)) return null;
        var host = uri.IdnHost;
        return Uri.CheckHostName(host) == UriHostNameType.Dns ? host : null;
    }

    private async Task RecordBlockedDomainAsync(string domain, CancellationToken cancellationToken)
    {
        if (_scopeFactory is null) return;
        try
        {
            using var scope = _scopeFactory.CreateScope();
            await scope.ServiceProvider.GetRequiredService<IBlockedAttemptRecorder>()
                .RecordBlockedDomainAsync(domain, DateTimeOffset.UtcNow, cancellationToken);
        }
        catch (Exception ex) { _logger.Error("EduFocus could not record a locally observed blocked domain attempt.", ex); }
    }

    private static IReadOnlySet<string> GetBlockedDomains(IReadOnlyCollection<BlockRule> rules, DateTimeOffset now)
    {
        var applicable = rules.Where(rule => rule.RuleType == BlockRuleType.Domain && rule.MatchType == BlockMatchType.Exact && rule.IsApplicable(now)).ToArray();
        return new HashSet<string>(applicable.Where(rule => rule.Effect == BlockRuleEffect.Block)
            .Where(rule => !applicable.Any(allow => allow.Effect == BlockRuleEffect.Allow && allow.NormalizedValue == rule.NormalizedValue))
            .Select(rule => rule.NormalizedValue), StringComparer.OrdinalIgnoreCase);
    }
}
