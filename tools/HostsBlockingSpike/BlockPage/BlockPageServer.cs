using System.Collections.Concurrent;
using System.Net;
using System.Net.Sockets;
using System.Text;

namespace HostsBlockingSpike.BlockPage;

public sealed record BlockPageRequest(
    DateTimeOffset TimestampUtc,
    string Method,
    string Host,
    string Target,
    string? UserAgent,
    int StatusCode);

public sealed class BlockPageServer : IDisposable
{
    private const int MaxHeaderBytes = 16 * 1024;
    private static readonly TimeSpan HeaderReadTimeout = TimeSpan.FromSeconds(5);

    private readonly HashSet<string> _blockedDomains;
    private readonly TcpListener _listener;
    private readonly ConcurrentQueue<BlockPageRequest> _requests = new();
    private readonly CancellationTokenSource _cts = new();
    private Task? _acceptLoop;

    public BlockPageServer(IEnumerable<string> blockedDomains, int port)
    {
        _blockedDomains = blockedDomains
            .Select(domain => domain.Trim().TrimEnd('.').ToLowerInvariant())
            .ToHashSet(StringComparer.Ordinal);
        Port = port;
        _listener = new TcpListener(IPAddress.Loopback, port);
    }

    public int Port { get; }

    public bool IsRunning { get; private set; }

    public IReadOnlyList<BlockPageRequest> Requests => _requests.ToArray();

    public void Start()
    {
        _listener.Start(backlog: 50);
        IsRunning = true;
        _acceptLoop = Task.Run(() => AcceptLoopAsync(_cts.Token));
    }

    public void Stop()
    {
        if (!IsRunning)
            return;

        _cts.Cancel();
        try { _listener.Stop(); }
        catch (SocketException) { }
        catch (ObjectDisposedException) { }

        try { _acceptLoop?.Wait(TimeSpan.FromSeconds(2)); }
        catch (AggregateException) { }

        IsRunning = false;
    }

    public void Dispose()
    {
        Stop();
        _cts.Dispose();
    }

    public static string BuildBlockPage(string host) => $$"""
        <!doctype html>
        <html lang="vi">
        <head>
        <meta charset="utf-8">
        <meta name="viewport" content="width=device-width, initial-scale=1">
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
        strong { color: #1f2933; }
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
        <p>Tên miền: <strong>{{WebUtility.HtmlEncode(host)}}</strong></p>
        <p>Hãy quay lại việc học hoặc nhờ phụ huynh mở khóa nếu thật sự cần thiết.</p>
        </main>
        </body>
        </html>
        """;

    private async Task AcceptLoopAsync(CancellationToken ct)
    {
        while (!ct.IsCancellationRequested)
        {
            TcpClient client;
            try
            {
                client = await _listener.AcceptTcpClientAsync(ct).ConfigureAwait(false);
            }
            catch (OperationCanceledException) { break; }
            catch (ObjectDisposedException) { break; }
            catch (SocketException) { break; }

            _ = HandleClientAsync(client, ct);
        }
    }

    private async Task HandleClientAsync(TcpClient client, CancellationToken ct)
    {
        using (client)
        {
            client.ReceiveTimeout = 5000;
            client.SendTimeout = 5000;

            await using var stream = client.GetStream();
            var request = await ReadRequestAsync(stream, ct).ConfigureAwait(false);
            if (request is null)
                return;

            var host = NormalizeHost(request.Headers.GetValueOrDefault("host"));
            var blocked = host is not null && _blockedDomains.Contains(host);
            var statusCode = blocked ? 200 : 404;
            var body = blocked && host is not null ? BuildBlockPage(host) : BuildNotFoundPage();
            var bodyBytes = Encoding.UTF8.GetBytes(body);
            var headerBytes = Encoding.ASCII.GetBytes(
                $"HTTP/1.1 {statusCode} {(blocked ? "OK" : "Not Found")}\r\n" +
                "Content-Type: text/html; charset=utf-8\r\n" +
                $"Content-Length: {bodyBytes.Length}\r\n" +
                "Cache-Control: no-store\r\n" +
                "X-Content-Type-Options: nosniff\r\n" +
                "Connection: close\r\n\r\n");

            _requests.Enqueue(new BlockPageRequest(
                DateTimeOffset.UtcNow,
                request.Method,
                host ?? string.Empty,
                request.Target,
                request.Headers.GetValueOrDefault("user-agent"),
                statusCode));

            try
            {
                await stream.WriteAsync(headerBytes, ct).ConfigureAwait(false);
                if (!string.Equals(request.Method, "HEAD", StringComparison.OrdinalIgnoreCase))
                    await stream.WriteAsync(bodyBytes, ct).ConfigureAwait(false);
                await stream.FlushAsync(ct).ConfigureAwait(false);
            }
            catch (IOException) { }
            catch (OperationCanceledException) { }
        }
    }

    private static async Task<ParsedRequest?> ReadRequestAsync(NetworkStream stream, CancellationToken ct)
    {
        using var buffer = new MemoryStream();
        var chunk = new byte[1024];

        while (buffer.Length < MaxHeaderBytes)
        {
            int read;
            try
            {
                read = await stream.ReadAsync(chunk, ct)
                    .AsTask()
                    .WaitAsync(HeaderReadTimeout, ct)
                    .ConfigureAwait(false);
            }
            catch (TimeoutException) { return null; }
            catch (OperationCanceledException) { return null; }
            catch (IOException) { return null; }

            if (read == 0)
                return null;

            buffer.Write(chunk, 0, read);
            var data = buffer.GetBuffer().AsSpan(0, (int)buffer.Length);
            var headerEnd = data.IndexOf("\r\n\r\n"u8);
            if (headerEnd < 0)
                continue;

            var headerText = Encoding.ASCII.GetString(data[..headerEnd]);
            var lines = headerText.Split("\r\n", StringSplitOptions.RemoveEmptyEntries);
            if (lines.Length == 0)
                return null;

            var requestLine = lines[0].Split(' ', StringSplitOptions.RemoveEmptyEntries);
            if (requestLine.Length < 3)
                return null;

            var headers = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
            foreach (var line in lines.Skip(1))
            {
                var separator = line.IndexOf(':');
                if (separator <= 0)
                    continue;
                headers[line[..separator].Trim()] = line[(separator + 1)..].Trim();
            }

            return new ParsedRequest(requestLine[0], requestLine[1], headers);
        }

        return null;
    }

    private static string? NormalizeHost(string? hostHeader)
    {
        if (string.IsNullOrWhiteSpace(hostHeader))
            return null;

        var host = hostHeader.Trim();
        if (host.StartsWith('['))
            return null;

        var colon = host.LastIndexOf(':');
        if (colon > 0 && host.IndexOf(':') == colon)
            host = host[..colon];

        host = host.TrimEnd('.').ToLowerInvariant();
        if (host.Length is 0 or > 253)
            return null;
        if (host.Any(c => !char.IsAsciiLetterOrDigit(c) && c is not ('.' or '-')))
            return null;

        return host;
    }

    private static string BuildNotFoundPage() => """
        <!doctype html>
        <html lang="vi">
        <head><meta charset="utf-8"><title>Not found</title></head>
        <body><p>Not found.</p></body>
        </html>
        """;

    private sealed record ParsedRequest(
        string Method,
        string Target,
        Dictionary<string, string> Headers);
}
