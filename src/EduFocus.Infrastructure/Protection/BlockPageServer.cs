using System.Collections.Frozen;
using System.Globalization;
using System.Net;
using System.Net.Sockets;
using System.Text;
using EduFocus.Domain.ValueObjects;
using EduFocus.Ports.Outbound.Services;

namespace EduFocus.Infrastructure.Protection;

/// <summary>
/// Minimal loopback HTTP server for the block page. It uses a raw
/// <see cref="TcpListener"/> instead of HttpListener so it does not need a
/// URL ACL reservation and only ever accepts connections from 127.0.0.1.
/// </summary>
public sealed class BlockPageServer : IBlockPageServer, IAsyncDisposable
{
    private const string ContentSecurityPolicy =
        "default-src 'none'; style-src 'unsafe-inline'; base-uri 'none'; form-action 'none'; frame-ancestors 'none'";

    private static readonly char[] ForbiddenHostCharacters =
        ['/', '\\', '@', '?', '#', ',', ';', '"', '\'', '<', '>', ' ', '\t'];

    private readonly BlockPageOptions _options;
    private readonly IAppLogger _logger;
    private readonly SemaphoreSlim _gate = new(1, 1);
    private readonly SemaphoreSlim _connectionSlots;

    private FrozenSet<string> _blockedDomains = FrozenSet<string>.Empty;
    private TcpListener? _listener;
    private CancellationTokenSource? _cts;
    private Task? _acceptLoop;
    private int _port;

    public BlockPageServer(BlockPageOptions options, IAppLogger logger)
    {
        ArgumentNullException.ThrowIfNull(options);
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));

        if (options.Port is < 0 or > 65535)
            throw new ArgumentOutOfRangeException(nameof(options), "Port must be between 0 and 65535.");
        if (options.MaxHeaderBytes is < 256 or > 64 * 1024)
            throw new ArgumentOutOfRangeException(nameof(options), "MaxHeaderBytes must be between 256 and 65536.");
        if (options.HeaderReadTimeout <= TimeSpan.Zero || options.HeaderReadTimeout > TimeSpan.FromSeconds(30))
            throw new ArgumentOutOfRangeException(nameof(options), "HeaderReadTimeout must be between 0 and 30 seconds.");
        if (options.MaxConcurrentConnections is < 1 or > 256)
            throw new ArgumentOutOfRangeException(nameof(options), "MaxConcurrentConnections must be between 1 and 256.");
        if (options.Backlog is < 1 or > 512)
            throw new ArgumentOutOfRangeException(nameof(options), "Backlog must be between 1 and 512.");

        _options = options;
        _connectionSlots = new SemaphoreSlim(
            options.MaxConcurrentConnections,
            options.MaxConcurrentConnections);
    }

    public bool IsRunning => Volatile.Read(ref _listener) is not null;

    public int Port => Volatile.Read(ref _port);

    public event Action<BlockPageHit>? BlockedRequestObserved;

    public async Task<BlockPageStartResult> StartAsync(
        IReadOnlyCollection<string> blockedDomains,
        CancellationToken cancellationToken = default)
    {
        var domains = NormalizeDomains(blockedDomains);

        await _gate.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            Volatile.Write(ref _blockedDomains, domains);

            if (_listener is not null)
                return new BlockPageStartResult(true, Port, null);

            var listener = new TcpListener(IPAddress.Loopback, _options.Port);
            try
            {
                listener.Start(_options.Backlog);
            }
            catch (SocketException ex)
            {
                try { listener.Stop(); }
                catch (SocketException) { }

                return FailStart(ex.SocketErrorCode);
            }

            var port = ((IPEndPoint)listener.LocalEndpoint).Port;
            var cts = new CancellationTokenSource();

            _cts = cts;
            _listener = listener;
            Volatile.Write(ref _port, port);
            _acceptLoop = Task.Run(() => AcceptLoopAsync(listener, cts.Token));

            _logger.Info(
                LogCategories.Protection,
                string.Create(CultureInfo.InvariantCulture, $"Block page listening on 127.0.0.1:{port}."));

            return new BlockPageStartResult(true, port, null);
        }
        finally
        {
            _gate.Release();
        }
    }

    public void UpdateDomains(IReadOnlyCollection<string> blockedDomains) =>
        Volatile.Write(ref _blockedDomains, NormalizeDomains(blockedDomains));

    public async Task StopAsync(CancellationToken cancellationToken = default)
    {
        await _gate.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            var listener = _listener;
            if (listener is null)
                return;

            var cts = _cts;
            var loop = _acceptLoop;

            _listener = null;
            _cts = null;
            _acceptLoop = null;

            cts?.Cancel();
            try { listener.Stop(); }
            catch (SocketException) { }
            catch (ObjectDisposedException) { }

            if (loop is not null)
            {
                try
                {
                    await loop.WaitAsync(TimeSpan.FromSeconds(2)).ConfigureAwait(false);
                }
                catch (TimeoutException)
                {
                    _logger.Warning(LogCategories.Protection, "Block page accept loop did not stop in time.");
                }
                catch (Exception ex)
                {
                    _logger.Error(LogCategories.Protection, "Block page accept loop faulted.", ex);
                }
            }

            cts?.Dispose();
            Volatile.Write(ref _port, 0);
            _logger.Info(LogCategories.Protection, "Block page listener stopped.");
        }
        finally
        {
            _gate.Release();
        }
    }

    public async ValueTask DisposeAsync() => await StopAsync().ConfigureAwait(false);

    private BlockPageStartResult FailStart(SocketError error)
    {
        var warning = error switch
        {
            SocketError.AddressAlreadyInUse =>
                $"Cổng {_options.Port} đang bị chiếm; block page không khả dụng nhưng trạng thái bảo vệ không thay đổi.",
            SocketError.AccessDenied =>
                $"Không đủ quyền mở cổng {_options.Port}; block page không khả dụng. Hãy chạy EduFocus với quyền Administrator.",
            _ =>
                $"Không mở được block page trên cổng {_options.Port}; trạng thái bảo vệ không thay đổi."
        };

        _logger.Warning(
            LogCategories.Protection,
            string.Create(
                CultureInfo.InvariantCulture,
                $"Block page listener failed on 127.0.0.1:{_options.Port} ({error}); protection continues without the local block page."));

        return new BlockPageStartResult(false, 0, warning);
    }

    private async Task AcceptLoopAsync(TcpListener listener, CancellationToken cancellationToken)
    {
        while (!cancellationToken.IsCancellationRequested)
        {
            TcpClient client;
            try
            {
                client = await listener.AcceptTcpClientAsync(cancellationToken).ConfigureAwait(false);
            }
            catch (OperationCanceledException) { break; }
            catch (SocketException) { break; }
            catch (ObjectDisposedException) { break; }

            if (!_connectionSlots.Wait(0))
            {
                client.Dispose();
                continue;
            }

            _ = HandleClientAsync(client, cancellationToken);
        }
    }

    private async Task HandleClientAsync(TcpClient client, CancellationToken cancellationToken)
    {
        try
        {
            using (client)
            {
                client.NoDelay = true;
                await using var stream = client.GetStream();

                var request = await ReadRequestAsync(stream, cancellationToken).ConfigureAwait(false);
                if (request is null)
                    return;

                var isHead = string.Equals(request.Method, "HEAD", StringComparison.Ordinal);
                if (!string.Equals(request.Method, "GET", StringComparison.Ordinal) && !isHead)
                {
                    await WriteResponseAsync(
                        stream, 405, "Method Not Allowed", BuildNotFoundPage(), includeBody: true, cancellationToken)
                        .ConfigureAwait(false);
                    return;
                }

                var includeBody = !isHead;
                var domain = ParseHost(request.Host);

                if (domain is null || !Volatile.Read(ref _blockedDomains).Contains(domain.Value))
                {
                    await WriteResponseAsync(
                        stream, 404, "Not Found", BuildNotFoundPage(), includeBody, cancellationToken)
                        .ConfigureAwait(false);
                    return;
                }

                RaiseHit(domain.Value);
                await WriteResponseAsync(
                    stream, 200, "OK", BuildBlockPage(domain.Value), includeBody, cancellationToken)
                    .ConfigureAwait(false);
            }
        }
        catch (IOException) { }
        catch (OperationCanceledException) { }
        catch (ObjectDisposedException) { }
        finally
        {
            _connectionSlots.Release();
        }
    }

    private void RaiseHit(string domain)
    {
        var handler = BlockedRequestObserved;
        if (handler is null)
            return;

        try
        {
            handler(new BlockPageHit(DateTimeOffset.UtcNow, domain));
        }
        catch (Exception ex)
        {
            _logger.Warning(
                LogCategories.Protection,
                $"Block page hit handler failed ({ex.GetType().Name}).");
        }
    }

    private async Task<ParsedRequest?> ReadRequestAsync(
        NetworkStream stream,
        CancellationToken cancellationToken)
    {
        using var buffer = new MemoryStream();
        var chunk = new byte[1024];

        while (true)
        {
            int read;
            try
            {
                read = await stream
                    .ReadAsync(chunk, cancellationToken)
                    .AsTask()
                    .WaitAsync(_options.HeaderReadTimeout, cancellationToken)
                    .ConfigureAwait(false);
            }
            catch (TimeoutException) { return null; }
            catch (OperationCanceledException) { return null; }
            catch (IOException) { return null; }
            catch (ObjectDisposedException) { return null; }

            if (read == 0)
                return null;

            buffer.Write(chunk, 0, read);

            if (buffer.Length > _options.MaxHeaderBytes)
                return null;

            var data = buffer.GetBuffer().AsSpan(0, (int)buffer.Length);
            var headerEnd = data.IndexOf("\r\n\r\n"u8);
            if (headerEnd < 0)
                continue;

            var headerText = Encoding.ASCII.GetString(data[..headerEnd]);
            return ParseRequest(headerText);
        }
    }

    private static ParsedRequest? ParseRequest(string headerText)
    {
        var lines = headerText.Split("\r\n", StringSplitOptions.None);
        if (lines.Length == 0)
            return null;

        var requestLine = lines[0].Split(' ', StringSplitOptions.RemoveEmptyEntries);
        if (requestLine.Length != 3)
            return null;
        if (!requestLine[2].StartsWith("HTTP/", StringComparison.Ordinal))
            return null;

        string? host = null;
        for (var i = 1; i < lines.Length; i++)
        {
            var line = lines[i];
            if (line.Length == 0)
                continue;

            var separator = line.IndexOf(':');
            if (separator <= 0)
                continue;

            var name = line[..separator].Trim();
            if (!name.Equals("Host", StringComparison.OrdinalIgnoreCase))
                continue;

            if (host is not null)
                return null;

            host = line[(separator + 1)..].Trim();
        }

        return string.IsNullOrEmpty(host)
            ? null
            : new ParsedRequest(requestLine[0], host);
    }

    private static DomainName? ParseHost(string hostHeader)
    {
        var value = hostHeader.Trim();
        if (value.Length is 0 or > 260)
            return null;
        if (value.IndexOfAny(ForbiddenHostCharacters) >= 0)
            return null;

        return DomainName.TryParse(value, out var domain) ? domain : null;
    }

    private static FrozenSet<string> NormalizeDomains(IReadOnlyCollection<string> blockedDomains)
    {
        ArgumentNullException.ThrowIfNull(blockedDomains);

        var set = new HashSet<string>(StringComparer.Ordinal);
        foreach (var raw in blockedDomains)
        {
            if (!DomainName.TryParse(raw, out var domain))
                throw new ArgumentException(
                    "Blocked domain list contains an invalid domain name.",
                    nameof(blockedDomains));

            set.Add(domain!.Value);
        }

        return set.ToFrozenSet(StringComparer.Ordinal);
    }

    private static async Task WriteResponseAsync(
        NetworkStream stream,
        int statusCode,
        string reason,
        string body,
        bool includeBody,
        CancellationToken cancellationToken)
    {
        var bodyBytes = Encoding.UTF8.GetBytes(body);
        var headerText = string.Create(
            CultureInfo.InvariantCulture,
            $"HTTP/1.1 {statusCode} {reason}\r\nContent-Type: text/html; charset=utf-8\r\nContent-Length: {bodyBytes.Length}\r\nCache-Control: no-store\r\nContent-Security-Policy: {ContentSecurityPolicy}\r\nX-Content-Type-Options: nosniff\r\nX-Frame-Options: DENY\r\nReferrer-Policy: no-referrer\r\nConnection: close\r\n\r\n");

        await stream.WriteAsync(Encoding.ASCII.GetBytes(headerText), cancellationToken).ConfigureAwait(false);
        if (includeBody)
            await stream.WriteAsync(bodyBytes, cancellationToken).ConfigureAwait(false);

        await stream.FlushAsync(cancellationToken).ConfigureAwait(false);
    }

    private static string BuildBlockPage(string domain) => $$"""
        <!doctype html>
        <html lang="vi">
        <head>
        <meta charset="utf-8">
        <meta name="viewport" content="width=device-width, initial-scale=1">
        <meta name="referrer" content="no-referrer">
        <title>Trang bị chặn - EduFocus</title>
        <style>
        :root { color-scheme: light dark; }
        body { margin: 0; min-height: 100vh; display: grid; place-items: center;
               font-family: "Segoe UI", system-ui, sans-serif; background: #f4f6fb; color: #1f2933; }
        main { max-width: 34rem; padding: 2.5rem; background: #fff; border-radius: 1rem;
               box-shadow: 0 12px 40px rgba(15, 23, 42, .12); }
        .badge { display: inline-block; padding: .25rem .75rem; border-radius: 999px;
                 background: #e0e7ff; color: #3730a3; font-weight: 600; font-size: .85rem; }
        h1 { font-size: 1.5rem; margin: 1rem 0 .75rem; line-height: 1.35; }
        p { margin: .5rem 0; line-height: 1.55; color: #475569; }
        strong { color: #1f2933; overflow-wrap: anywhere; }
        @media (prefers-color-scheme: dark) {
          body { background: #0f172a; color: #e2e8f0; }
          main { background: #1e293b; box-shadow: none; }
          .badge { background: #312e81; color: #c7d2fe; }
          p { color: #cbd5e1; }
        }
        </style>
        </head>
        <body>
        <main>
        <!-- EDUFOCUS_BLOCK_PAGE -->
        <span class="badge">EduFocus</span>
        <h1>Trang này không nằm trong môi trường học tập.</h1>
        <p>Tên miền: <strong>{{WebUtility.HtmlEncode(domain)}}</strong></p>
        <p>Hãy quay lại việc học hoặc nhờ phụ huynh mở khóa nếu thật sự cần thiết.</p>
        </main>
        </body>
        </html>
        """;

    private static string BuildNotFoundPage() => """
        <!doctype html>
        <html lang="vi">
        <head><meta charset="utf-8"><title>Không tìm thấy</title></head>
        <body><p>Không tìm thấy trang.</p></body>
        </html>
        """;

    private sealed record ParsedRequest(string Method, string Host);
}
