using System.Net;
using System.Net.Sockets;
using System.Text;
using EduFocus.Infrastructure.Protection;
using EduFocus.Ports.Outbound.Services;
using FluentAssertions;

namespace EduFocus.Infrastructure.Tests;

public sealed class BlockPageServerTests
{
    private readonly RecordingLogger _logger = new();

    [Fact]
    public async Task Start_binds_loopback_and_serves_blocked_domain()
    {
        await using var server = CreateServer();

        var start = await server.StartAsync(["chatgpt.com"]);

        start.Started.Should().BeTrue();
        start.Port.Should().BeGreaterThan(0);
        start.Warning.Should().BeNull();
        server.IsRunning.Should().BeTrue();
        server.Port.Should().Be(start.Port);

        var response = await SendAsync(start.Port, Request("GET", "chatgpt.com"));

        response.StatusCode.Should().Be(200);
        response.Body.Should().Contain("Trang này không nằm trong môi trường học tập.");
        response.Body.Should().Contain("chatgpt.com");
    }

    [Fact]
    public async Task Start_is_idempotent_and_refreshes_domains()
    {
        await using var server = CreateServer();
        var first = await server.StartAsync(["chatgpt.com"]);

        var second = await server.StartAsync(["claude.ai"]);

        second.Started.Should().BeTrue();
        second.Port.Should().Be(first.Port);

        var blocked = await SendAsync(first.Port, Request("GET", "claude.ai"));
        var removed = await SendAsync(first.Port, Request("GET", "chatgpt.com"));

        blocked.StatusCode.Should().Be(200);
        removed.StatusCode.Should().Be(404);
    }

    [Fact]
    public async Task Blocked_response_has_static_security_headers()
    {
        await using var server = CreateServer();
        var start = await server.StartAsync(["chatgpt.com"]);

        var response = await SendAsync(start.Port, Request("GET", "chatgpt.com"));

        response.Headers["content-type"].Should().Be("text/html; charset=utf-8");
        response.Headers["cache-control"].Should().Be("no-store");
        response.Headers["x-content-type-options"].Should().Be("nosniff");
        response.Headers["x-frame-options"].Should().Be("DENY");
        response.Headers["referrer-policy"].Should().Be("no-referrer");
        response.Headers["content-security-policy"]
            .Should().Contain("default-src 'none'")
            .And.Contain("style-src 'unsafe-inline'")
            .And.Contain("frame-ancestors 'none'");
    }

    [Fact]
    public async Task Blocked_page_does_not_reference_external_resources()
    {
        await using var server = CreateServer();
        var start = await server.StartAsync(["chatgpt.com"]);

        var response = await SendAsync(start.Port, Request("GET", "chatgpt.com"));

        response.Body.Should().NotContain("http://");
        response.Body.Should().NotContain("https://");
        response.Body.Should().NotContain("src=");
        response.Body.Should().NotContain("href=");
        response.Body.Should().NotContain("<script");
        response.Body.Should().NotContain("<img");
    }

    [Fact]
    public async Task Blocked_request_raises_hit_with_domain_only()
    {
        await using var server = CreateServer();
        var hits = new List<BlockPageHit>();
        server.BlockedRequestObserved += hits.Add;
        var start = await server.StartAsync(["ChatGPT.com."]);

        var response = await SendAsync(
            start.Port,
            Request("GET", "chatgpt.com", "/chat?q=secret-token#fragment"));

        response.StatusCode.Should().Be(200);
        hits.Should().ContainSingle();
        hits[0].Domain.Should().Be("chatgpt.com");
        hits[0].ObservedAtUtc.Should().BeCloseTo(DateTimeOffset.UtcNow, TimeSpan.FromSeconds(5));
        response.Body.Should().NotContain("secret-token");
        response.Body.Should().NotContain("fragment");
    }

    [Fact]
    public async Task Host_header_injection_returns_generic_not_found()
    {
        await using var server = CreateServer();
        var start = await server.StartAsync(["chatgpt.com"]);

        var response = await SendAsync(
            start.Port,
            Request("GET", "chatgpt.com\"><script>alert(1)</script>"));

        response.StatusCode.Should().Be(404);
        response.Body.Should().NotContain("chatgpt.com");
        response.Body.Should().NotContain("<script");
        response.Body.Should().NotContain("alert");
    }

    [Fact]
    public async Task Path_and_query_injection_are_not_reflected()
    {
        await using var server = CreateServer();
        var start = await server.StartAsync(["chatgpt.com"]);

        var response = await SendAsync(
            start.Port,
            Request(
                "GET",
                "chatgpt.com",
                "/\"><script>alert(1)</script>?q=%3Cimg%20src=x%20onerror=alert(1)%3E"));

        response.StatusCode.Should().Be(200);
        response.Body.Should().Contain("chatgpt.com");
        response.Body.Should().NotContain("<script");
        response.Body.Should().NotContain("alert");
        response.Body.Should().NotContain("onerror");
        response.Body.Should().NotContain("<img");
    }

    [Fact]
    public async Task Unknown_host_returns_generic_not_found_without_echo()
    {
        await using var server = CreateServer();
        var start = await server.StartAsync(["chatgpt.com"]);

        var response = await SendAsync(start.Port, Request("GET", "example.org"));

        response.StatusCode.Should().Be(404);
        response.Body.Should().NotContain("example.org");
        response.Body.Should().Contain("Không tìm thấy trang.");
    }

    [Fact]
    public async Task Head_request_returns_headers_without_body_and_records_hit()
    {
        await using var server = CreateServer();
        var hits = 0;
        server.BlockedRequestObserved += _ => Interlocked.Increment(ref hits);
        var start = await server.StartAsync(["chatgpt.com"]);

        var response = await SendAsync(start.Port, Request("HEAD", "chatgpt.com"));

        response.StatusCode.Should().Be(200);
        response.Body.Should().BeEmpty();
        response.Headers["content-length"].Should().NotBe("0");
        hits.Should().Be(1);
    }

    [Fact]
    public async Task Non_get_or_head_method_is_rejected_without_hit()
    {
        await using var server = CreateServer();
        var hits = 0;
        server.BlockedRequestObserved += _ => Interlocked.Increment(ref hits);
        var start = await server.StartAsync(["chatgpt.com"]);

        var response = await SendAsync(start.Port, Request("POST", "chatgpt.com"));

        response.StatusCode.Should().Be(405);
        hits.Should().Be(0);
    }

    [Fact]
    public async Task Malformed_request_without_host_is_closed()
    {
        await using var server = CreateServer();
        var start = await server.StartAsync(["chatgpt.com"]);

        var response = await SendAsync(
            start.Port,
            "GET / HTTP/1.1\r\nConnection: close\r\n\r\n");

        response.Raw.Should().BeEmpty();
    }

    [Fact]
    public async Task Oversized_header_is_rejected()
    {
        await using var server = CreateServer(new BlockPageOptions
        {
            Port = 0,
            MaxHeaderBytes = 256,
            HeaderReadTimeout = TimeSpan.FromSeconds(2)
        });
        var start = await server.StartAsync(["chatgpt.com"]);

        var response = await SendAsync(start.Port, new string('A', 1024));

        response.Raw.Should().BeEmpty();
    }

    [Fact]
    public async Task Port_conflict_returns_warning_without_throwing()
    {
        var occupied = new TcpListener(IPAddress.Loopback, 0);
        occupied.Start();
        var port = ((IPEndPoint)occupied.LocalEndpoint).Port;

        try
        {
            await using var server = CreateServer(new BlockPageOptions { Port = port });

            var result = await server.StartAsync(["chatgpt.com"]);

            result.Started.Should().BeFalse();
            result.Port.Should().Be(0);
            result.Warning.Should().Contain(port.ToString());
            result.Warning.Should().Contain("block page");
            server.IsRunning.Should().BeFalse();
            _logger.Messages.Should().Contain(message => message.Contains("Block page listener failed"));
        }
        finally
        {
            occupied.Stop();
        }

        await using var retry = CreateServer(new BlockPageOptions { Port = port });
        var restarted = await retry.StartAsync(["chatgpt.com"]);
        restarted.Started.Should().BeTrue();
        restarted.Port.Should().Be(port);
    }

    [Fact]
    public async Task Stop_is_idempotent_and_restart_works()
    {
        var server = CreateServer();
        await server.StartAsync(["chatgpt.com"]);

        await server.StopAsync();
        server.IsRunning.Should().BeFalse();
        server.Port.Should().Be(0);

        await server.StopAsync();

        var second = await server.StartAsync(["chatgpt.com"]);
        second.Started.Should().BeTrue();
        var response = await SendAsync(second.Port, Request("GET", "chatgpt.com"));
        response.StatusCode.Should().Be(200);

        await server.DisposeAsync();
    }

    [Fact]
    public async Task Update_domains_while_running_affects_next_requests()
    {
        await using var server = CreateServer();
        var start = await server.StartAsync(["chatgpt.com"]);

        server.UpdateDomains(["claude.ai"]);

        var removed = await SendAsync(start.Port, Request("GET", "chatgpt.com"));
        var added = await SendAsync(start.Port, Request("GET", "claude.ai"));

        removed.StatusCode.Should().Be(404);
        added.StatusCode.Should().Be(200);
    }

    [Fact]
    public async Task Invalid_domains_are_rejected_before_listening()
    {
        await using var server = CreateServer();

        var update = () => server.UpdateDomains(["not a domain"]);
        var start = () => server.StartAsync(["localhost"]);

        update.Should().Throw<ArgumentException>();
        await start.Should().ThrowAsync<ArgumentException>();
        server.IsRunning.Should().BeFalse();
    }

    [Fact]
    public async Task Hit_handler_failure_does_not_break_the_page()
    {
        await using var server = CreateServer();
        server.BlockedRequestObserved += _ => throw new InvalidOperationException("boom");
        var start = await server.StartAsync(["chatgpt.com"]);

        var response = await SendAsync(start.Port, Request("GET", "chatgpt.com"));

        response.StatusCode.Should().Be(200);
        response.Body.Should().Contain("Trang này không nằm trong môi trường học tập.");
        _logger.Messages.Should().Contain(message => message.Contains("hit handler failed"));
        _logger.Messages.Should().NotContain(message => message.Contains("boom"));
    }

    [Fact]
    public async Task Logs_never_contain_blocked_domain_or_target()
    {
        await using var server = CreateServer();
        var start = await server.StartAsync(["chatgpt.com"]);

        await SendAsync(start.Port, Request("GET", "chatgpt.com", "/secret?token=abc"));
        await server.StopAsync();

        _logger.Messages.Should().NotContain(message => message.Contains("chatgpt.com"));
        _logger.Messages.Should().NotContain(message => message.Contains("secret"));
        _logger.Messages.Should().NotContain(message => message.Contains("token"));
    }

    private BlockPageServer CreateServer(BlockPageOptions? options = null) =>
        new(options ?? new BlockPageOptions { Port = 0 }, _logger);

    private static string Request(string method, string host, string target = "/") =>
        $"{method} {target} HTTP/1.1\r\nHost: {host}\r\nConnection: close\r\n\r\n";

    private static async Task<TestResponse> SendAsync(
        int port,
        string request,
        int timeoutMilliseconds = 5000)
    {
        using var timeout = new CancellationTokenSource(timeoutMilliseconds);
        using var client = new TcpClient();
        await client.ConnectAsync(IPAddress.Loopback, port, timeout.Token);

        await using var stream = client.GetStream();
        await stream.WriteAsync(Encoding.ASCII.GetBytes(request), timeout.Token);
        await stream.FlushAsync(timeout.Token);

        using var buffer = new MemoryStream();
        var chunk = new byte[4096];
        while (true)
        {
            var read = await stream.ReadAsync(chunk, timeout.Token);
            if (read == 0)
                break;

            buffer.Write(chunk, 0, read);
        }

        return TestResponse.Parse(buffer.ToArray());
    }

    private sealed record TestResponse(
        int StatusCode,
        Dictionary<string, string> Headers,
        string Body,
        byte[] Raw)
    {
        public static TestResponse Parse(byte[] raw)
        {
            var separator = raw.AsSpan().IndexOf("\r\n\r\n"u8);
            if (separator < 0)
                return new TestResponse(0, [], string.Empty, raw);

            var headerText = Encoding.ASCII.GetString(raw.AsSpan(0, separator));
            var lines = headerText.Split("\r\n");
            var statusParts = lines[0].Split(' ', StringSplitOptions.RemoveEmptyEntries);
            var status = statusParts.Length > 1 && int.TryParse(statusParts[1], out var parsed)
                ? parsed
                : 0;

            var headers = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
            foreach (var line in lines.Skip(1))
            {
                var colon = line.IndexOf(':');
                if (colon > 0)
                    headers[line[..colon].Trim()] = line[(colon + 1)..].Trim();
            }

            var body = Encoding.UTF8.GetString(raw.AsSpan(separator + 4));
            return new TestResponse(status, headers, body, raw);
        }
    }

    private sealed class RecordingLogger : IAppLogger
    {
        private readonly List<string> _messages = [];

        public IReadOnlyList<string> Messages
        {
            get
            {
                lock (_messages)
                    return _messages.ToArray();
            }
        }

        public void Info(string category, string message) => Add(message);

        public void Warning(string category, string message) => Add(message);

        public void Error(string category, string message, Exception? exception = null) => Add(message);

        private void Add(string message)
        {
            lock (_messages)
                _messages.Add(message);
        }
    }
}
