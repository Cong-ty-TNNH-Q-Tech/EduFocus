using System.Diagnostics;
using System.Net;
using System.Net.Sockets;
using System.Text;
using EduFocus.Infrastructure.Protection;
using EduFocus.Ports.Outbound.Services;
using FluentAssertions;
using Xunit.Abstractions;

namespace EduFocus.Infrastructure.Tests;

/// <summary>
/// BA Document §9.3 requires a loopback block page under 100 ms and no
/// listener leak across protection start/stop cycles.
/// </summary>
public sealed class BlockPagePerformanceTests
{
    private readonly ITestOutputHelper _output;

    public BlockPagePerformanceTests(ITestOutputHelper output) => _output = output;

    [Fact]
    public async Task Loopback_block_page_responds_within_the_hundred_millisecond_budget()
    {
        await using var server = CreateServer();
        var start = await server.StartAsync(["chatgpt.com"]);

        for (var index = 0; index < 3; index++)
            (await SendAsync(start.Port)).StatusCode.Should().Be(200);

        var samples = new List<double>();
        for (var index = 0; index < 20; index++)
        {
            var stopwatch = Stopwatch.StartNew();
            var response = await SendAsync(start.Port);
            stopwatch.Stop();
            response.StatusCode.Should().Be(200);
            samples.Add(stopwatch.Elapsed.TotalMilliseconds);
        }

        var sorted = samples.OrderBy(value => value).ToArray();
        var median = sorted[sorted.Length / 2];
        var p95 = sorted[(int)Math.Ceiling(sorted.Length * 0.95) - 1];
        var max = sorted[^1];

        _output.WriteLine(
            $"Block page loopback latency over {sorted.Length} requests: " +
            $"median {median:F2} ms, p95 {p95:F2} ms, max {max:F2} ms.");

        median.Should().BeLessThan(50);
        p95.Should().BeLessThan(100, "BA §9.3 requires the loopback block page under 100 ms");
    }

    [Fact]
    public async Task Listener_survives_many_start_stop_cycles_without_leaking()
    {
        await using var server = CreateServer();

        for (var cycle = 0; cycle < 20; cycle++)
        {
            var start = await server.StartAsync(["chatgpt.com"]);

            start.Started.Should().BeTrue();
            server.IsRunning.Should().BeTrue();
            (await SendAsync(start.Port)).StatusCode.Should().Be(200);

            await server.StopAsync();
            server.IsRunning.Should().BeFalse();
        }

        var restarted = await server.StartAsync(["chatgpt.com"]);
        restarted.Started.Should().BeTrue();
        (await SendAsync(restarted.Port)).StatusCode.Should().Be(200);
    }

    private static BlockPageServer CreateServer() =>
        new(new BlockPageOptions { Port = 0 }, new SilentLogger());

    private static async Task<TestResponse> SendAsync(int port, int timeoutMilliseconds = 5000)
    {
        using var timeout = new CancellationTokenSource(timeoutMilliseconds);
        using var client = new TcpClient();
        await client.ConnectAsync(IPAddress.Loopback, port, timeout.Token);

        await using var stream = client.GetStream();
        var request = Encoding.ASCII.GetBytes(
            "GET / HTTP/1.1\r\nHost: chatgpt.com\r\nConnection: close\r\n\r\n");
        await stream.WriteAsync(request, timeout.Token);
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

        var raw = buffer.ToArray();
        var separator = raw.AsSpan().IndexOf("\r\n\r\n"u8);
        if (separator < 0)
            return new TestResponse(0, string.Empty);

        var statusLine = Encoding.ASCII.GetString(raw.AsSpan(0, separator)).Split("\r\n")[0];
        var parts = statusLine.Split(' ', StringSplitOptions.RemoveEmptyEntries);
        var status = parts.Length > 1 && int.TryParse(parts[1], out var parsed) ? parsed : 0;
        return new TestResponse(status, Encoding.UTF8.GetString(raw.AsSpan(separator + 4)));
    }

    private sealed record TestResponse(int StatusCode, string Body);

    private sealed class SilentLogger : IAppLogger
    {
        public void Info(string category, string message)
        {
        }

        public void Warning(string category, string message)
        {
        }

        public void Error(string category, string message, Exception? exception = null)
        {
        }
    }
}
