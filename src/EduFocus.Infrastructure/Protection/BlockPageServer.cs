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
        :root {
          color-scheme: light dark;
          --bg-a: #eef1ff;
          --bg-b: #f8f9fe;
          --card: #ffffff;
          --text: #1b1f31;
          --muted: #5a6174;
          --primary: #5b5ce2;
          --primary-soft: #eeeeff;
          --primary-ink: #3f3fc4;
          --border: #e4e7f2;
          --soft: #f1f3fa;
        }
        * { box-sizing: border-box; }
        body {
          margin: 0;
          min-height: 100vh;
          display: grid;
          place-items: center;
          padding: 2rem 1.25rem;
          font-family: "Segoe UI", system-ui, -apple-system, sans-serif;
          background:
            radial-gradient(circle at 15% 10%, rgba(91, 92, 226, .16), transparent 45%),
            radial-gradient(circle at 90% 85%, rgba(255, 180, 40, .14), transparent 40%),
            linear-gradient(160deg, var(--bg-a), var(--bg-b));
          color: var(--text);
        }
        main {
          width: min(38rem, 100%);
          padding: clamp(1.5rem, 4vw, 2.5rem);
          background: var(--card);
          border: 1px solid var(--border);
          border-radius: 1.5rem;
          box-shadow: 0 24px 60px rgba(27, 31, 49, .14);
        }
        .brand { display: flex; align-items: center; gap: .7rem; }
        .mark {
          display: grid;
          place-items: center;
          width: 2.75rem;
          height: 2.75rem;
          border-radius: .9rem;
          background: linear-gradient(145deg, #5b5ce2, #8b7cf8);
          color: #fff;
          font-size: 1.25rem;
          font-weight: 700;
          letter-spacing: .02em;
        }
        .wordmark { font-size: 1rem; font-weight: 600; letter-spacing: .01em; }
        .badge {
          display: inline-flex;
          align-items: center;
          gap: .45rem;
          margin-top: 1.35rem;
          padding: .32rem .8rem;
          border-radius: 999px;
          background: var(--primary-soft);
          color: var(--primary-ink);
          font-size: .82rem;
          font-weight: 600;
        }
        .dot {
          width: .5rem;
          height: .5rem;
          border-radius: 50%;
          background: #12b76a;
          box-shadow: 0 0 0 3px rgba(18, 183, 106, .18);
        }
        h1 {
          margin: .85rem 0 .65rem;
          font-size: clamp(1.35rem, 4vw, 1.7rem);
          line-height: 1.35;
          letter-spacing: -.01em;
        }
        .lead { margin: 0; color: var(--muted); font-size: .98rem; line-height: 1.6; }
        .domain {
          margin-top: 1.1rem;
          padding: .7rem .9rem;
          border-radius: .8rem;
          background: var(--soft);
          border: 1px dashed var(--border);
          color: var(--muted);
          font-size: .85rem;
        }
        .domain strong {
          color: var(--text);
          overflow-wrap: anywhere;
        }
        .tips {
          list-style: none;
          margin: 1.4rem 0 0;
          padding: 0;
          display: grid;
          gap: .65rem;
        }
        .tips li {
          display: flex;
          align-items: flex-start;
          gap: .75rem;
          padding: .8rem .9rem;
          border: 1px solid var(--border);
          border-radius: .9rem;
          background: var(--card);
        }
        .num {
          flex: 0 0 auto;
          display: grid;
          place-items: center;
          width: 1.65rem;
          height: 1.65rem;
          border-radius: 50%;
          background: var(--primary-soft);
          color: var(--primary-ink);
          font-size: .8rem;
          font-weight: 700;
        }
        .tips b { display: block; font-size: .92rem; }
        .tip-text {
          display: block;
          margin-top: .15rem;
          color: var(--muted);
          font-size: .84rem;
          line-height: 1.5;
        }
        .note {
          margin: 1.3rem 0 0;
          color: var(--muted);
          font-size: .8rem;
          line-height: 1.55;
        }
        footer {
          width: min(38rem, 100%);
          margin-top: 1.25rem;
          text-align: center;
          color: var(--muted);
          font-size: .78rem;
        }
        @media (prefers-color-scheme: dark) {
          :root {
            --bg-a: #10142a;
            --bg-b: #161b30;
            --card: #1b2033;
            --text: #eef0ff;
            --muted: #aab2cc;
            --primary: #9a9bff;
            --primary-soft: #272c48;
            --primary-ink: #c3c4ff;
            --border: #2c3350;
            --soft: #232840;
          }
          body {
            background:
              radial-gradient(circle at 15% 10%, rgba(124, 108, 246, .28), transparent 45%),
              radial-gradient(circle at 90% 85%, rgba(255, 180, 40, .12), transparent 40%),
              linear-gradient(160deg, var(--bg-a), var(--bg-b));
          }
          main { box-shadow: none; }
          .mark { background: linear-gradient(145deg, #6d5ce7, #9a7cf9); }
        }
        @media (max-width: 30rem) {
          body { padding: 1rem .75rem; }
          main { border-radius: 1.15rem; }
        }
        </style>
        </head>
        <body>
        <!-- EDUFOCUS_BLOCK_PAGE -->
        <main>
        <div class="brand"><span class="mark">E</span><span class="wordmark">EduFocus</span></div>
        <span class="badge"><span class="dot"></span>Đang bảo vệ</span>
        <h1>Trang này không nằm trong môi trường học tập.</h1>
        <p class="lead">Trang bạn vừa mở không nằm trong danh sách học tập được phép trên máy này.</p>
        <div class="domain">Tên miền: <strong>{{WebUtility.HtmlEncode(domain)}}</strong></div>
        <ul class="tips">
        <li><span class="num">1</span><div><b>Hoàn thành phần đang học</b><span class="tip-text">Quay lại bài đang dở rồi mới mở trang khác.</span></div></li>
        <li><span class="num">2</span><div><b>Hỏi phụ huynh nếu thật sự cần</b><span class="tip-text">Phụ huynh có thể kiểm tra hoặc tạm tắt bảo vệ trong Parent Mode.</span></div></li>
        <li><span class="num">3</span><div><b>Nghỉ mắt vài phút</b><span class="tip-text">Đứng dậy, uống nước rồi quay lại khi đã sẵn sàng.</span></div></li>
        </ul>
        <p class="note">Nếu bạn cho rằng đây là nhầm lẫn, hãy nhờ phụ huynh kiểm tra danh sách chặn. EduFocus chỉ chặn những gì được thiết lập trên chính máy này.</p>
        </main>
        <footer>EduFocus · Bảo vệ đang bật trên máy này</footer>
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
