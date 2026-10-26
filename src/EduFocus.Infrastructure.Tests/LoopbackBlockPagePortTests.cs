using System.Net;
using System.Net.Sockets;
using System.Text;
using EduFocus.Domain.Entities;
using EduFocus.Domain.Enums;
using EduFocus.Infrastructure.Services;
using EduFocus.Ports.Outbound.Services;

namespace EduFocus.Infrastructure.Tests;

public sealed class LoopbackBlockPagePortTests
{
    [Fact]
    public void Block_page_escapes_untrusted_domain_and_has_no_remote_assets_or_scripts()
    {
        var html = LoopbackBlockPagePort.BuildPage("<img src=x onerror=alert(1)>");

        Assert.Contains("&lt;img src=x onerror=alert(1)&gt;", html);
        Assert.DoesNotContain("<img src=x", html, StringComparison.Ordinal);
        Assert.DoesNotContain("<script", html, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("https://", html, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task Listener_serves_only_loopback_content_and_stops_cleanly()
    {
        var portNumber = GetFreeLoopbackPort();
        var prefix = $"http://127.0.0.1:{portNumber}/";
        var logger = new FakeLogger();
        await using var listener = new LoopbackBlockPagePort(logger, prefix);
        Assert.True(await listener.StartAsync([BlockRule.Create(BlockRuleType.Domain, BlockMatchType.Exact, "chatgpt.com",
            BlockRuleCategory.Chatbot, BlockRuleEffect.Block, DateTimeOffset.UtcNow)]));
        Assert.True(listener.IsAvailable);

        using var client = new HttpClient();
        using var request = new HttpRequestMessage(HttpMethod.Get, $"http://127.0.0.1:{portNumber}/path?token=secret");
        request.Headers.Host = "chatgpt.com";
        using var response = await client.SendAsync(request);
        var body = await response.Content.ReadAsStringAsync();
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Equal("text/html", response.Content.Headers.ContentType?.MediaType);
        Assert.Contains("chatgpt.com", body);
        Assert.DoesNotContain("token=secret", body);
        Assert.Contains("default-src 'none'", response.Headers.GetValues("Content-Security-Policy").Single());

        await listener.StopAsync();
        Assert.False(listener.IsAvailable);
    }

    [Fact]
    public async Task Port_conflict_returns_unavailable_without_throwing_or_stopping_existing_listener()
    {
        var portNumber = GetFreeLoopbackPort();
        var prefix = $"http://127.0.0.1:{portNumber}/";
        var logger = new FakeLogger();
        await using var first = new LoopbackBlockPagePort(logger, prefix);
        await using var second = new LoopbackBlockPagePort(logger, prefix);

        var rule = BlockRule.Create(BlockRuleType.Domain, BlockMatchType.Exact, "chatgpt.com", BlockRuleCategory.Chatbot, BlockRuleEffect.Block, DateTimeOffset.UtcNow);
        Assert.True(await first.StartAsync([rule]));
        Assert.False(await second.StartAsync([rule]));
        Assert.True(first.IsAvailable);
        Assert.False(second.IsAvailable);
        Assert.Contains(logger.Messages, message => message.Contains("could not bind", StringComparison.Ordinal));
    }

    [Fact]
    public void Listener_refuses_non_loopback_binding()
    {
        Assert.Throws<ArgumentException>(() => new LoopbackBlockPagePort(new FakeLogger(), "http://0.0.0.0:8080/"));
        Assert.Throws<ArgumentException>(() => new LoopbackBlockPagePort(new FakeLogger(), "http://example.com:8080/"));
    }

    [Fact]
    public async Task Listener_does_not_serve_or_record_hosts_that_are_not_currently_blocked()
    {
        var portNumber = GetFreeLoopbackPort();
        await using var listener = new LoopbackBlockPagePort(new FakeLogger(), $"http://127.0.0.1:{portNumber}/");
        Assert.True(await listener.StartAsync([BlockRule.Create(BlockRuleType.Domain, BlockMatchType.Exact, "chatgpt.com",
            BlockRuleCategory.Chatbot, BlockRuleEffect.Block, DateTimeOffset.UtcNow)]));
        using var client = new HttpClient();
        using var request = new HttpRequestMessage(HttpMethod.Get, $"http://127.0.0.1:{portNumber}/");
        request.Headers.Host = "school.example";
        using var response = await client.SendAsync(request);
        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
    }

    private static int GetFreeLoopbackPort()
    {
        using var socket = new TcpListener(IPAddress.Loopback, 0);
        socket.Start();
        var port = ((IPEndPoint)socket.LocalEndpoint).Port;
        socket.Stop();
        return port;
    }

    private sealed class FakeLogger : IAppLogger
    {
        public List<string> Messages { get; } = [];
        public void Info(string message) => Messages.Add(message);
        public void Warning(string message) => Messages.Add(message);
        public void Error(string message, Exception? ex = null) => Messages.Add(message);
    }
}
