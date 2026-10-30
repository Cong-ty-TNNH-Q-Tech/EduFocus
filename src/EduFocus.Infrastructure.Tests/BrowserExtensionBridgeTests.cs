using System.Buffers.Binary;
using System.IO.Pipes;
using System.Text;
using System.Text.Json;
using EduFocus.Domain.Entities;
using EduFocus.Domain.Enums;
using EduFocus.Infrastructure.Protection;
using EduFocus.Ports.Outbound.Services;
using FluentAssertions;

namespace EduFocus.Infrastructure.Tests;

public sealed class BrowserExtensionBridgeTests
{
    private static readonly DateTimeOffset Now = new(2026, 10, 30, 8, 0, 0, TimeSpan.Zero);

    [Fact]
    public async Task Start_publishes_the_rule_snapshot_to_a_connected_host()
    {
        await using var bridge = CreateBridge(out var options);

        await bridge.StartAsync(
            [DomainRule("chatgpt.com"), UrlRule("https://www.youtube.com/shorts/")],
            Now.UtcDateTime);

        await using var client = await ConnectAsync(options.PipeName);
        using var snapshot = await ReadJsonAsync(client);

        snapshot.RootElement.GetProperty("type").GetString().Should().Be("snapshot");
        snapshot.RootElement.GetProperty("protocol").GetInt32().Should().Be(1);
        snapshot.RootElement.GetProperty("generatedAtUtc").GetDateTimeOffset().Should().Be(Now);

        var rules = snapshot.RootElement.GetProperty("rules").EnumerateArray().ToArray();
        rules.Should().HaveCount(2);
        rules.Should().Contain(rule =>
            rule.GetProperty("type").GetString() == "domain"
            && rule.GetProperty("value").GetString() == "chatgpt.com"
            && rule.GetProperty("effect").GetString() == "block");
        rules.Should().Contain(rule =>
            rule.GetProperty("type").GetString() == "url"
            && rule.GetProperty("match").GetString() == "contains"
            && rule.GetProperty("value").GetString() == "https://www.youtube.com/shorts/");

        var protectedExact = snapshot.RootElement.GetProperty("protectedExact")
            .EnumerateArray().Select(value => value.GetString()).ToArray();
        protectedExact.Should().Contain("google.com");

        var protectedServices = snapshot.RootElement.GetProperty("protectedServices")
            .EnumerateArray().Select(value => value.GetString()).ToArray();
        protectedServices.Should().Contain("meet.google.com");
    }

    [Fact]
    public async Task Verified_blocked_message_raises_attempt_with_sanitized_url()
    {
        await using var bridge = CreateBridge(out var options);
        var attempts = new List<BrowserBlockedAttempt>();
        bridge.BlockedAttemptObserved += (_, attempt) =>
        {
            lock (attempts)
                attempts.Add(attempt);
        };

        await bridge.StartAsync([UrlRule("https://www.youtube.com/shorts/")], Now.UtcDateTime);
        await using var client = await ConnectAsync(options.PipeName);
        using var _ = await ReadJsonAsync(client);

        await SendAsync(
            client,
            """
            {"type":"blocked","url":"https://www.youtube.com/shorts/abc?si=tracking#top"}
            """);

        await WaitAsync(() =>
        {
            lock (attempts)
                return attempts.Count == 1;
        });

        lock (attempts)
        {
            attempts[0].Url.Should().Be("https://www.youtube.com/shorts/abc");
            attempts[0].ObservedAtUtc.Should().Be(Now);
        }
    }

    [Theory]
    [InlineData("https://example.com/lesson")]
    [InlineData("https://meet.google.com/abc-defg-hij")]
    [InlineData("not a url")]
    public async Task Blocked_message_that_does_not_match_the_snapshot_is_ignored(string url)
    {
        await using var bridge = CreateBridge(out var options);
        var raised = 0;
        bridge.BlockedAttemptObserved += (_, _) => Interlocked.Increment(ref raised);

        await bridge.StartAsync([UrlRule("https://www.youtube.com/shorts/")], Now.UtcDateTime);
        await using var client = await ConnectAsync(options.PipeName);
        using var _ = await ReadJsonAsync(client);

        await SendAsync(client, $$"""{"type":"blocked","url":"{{url}}"}""");
        await Task.Delay(200);

        raised.Should().Be(0);
    }

    [Fact]
    public async Task Malformed_message_does_not_drop_the_connection()
    {
        await using var bridge = CreateBridge(out var options);
        var attempts = new List<BrowserBlockedAttempt>();
        bridge.BlockedAttemptObserved += (_, attempt) =>
        {
            lock (attempts)
                attempts.Add(attempt);
        };

        await bridge.StartAsync([UrlRule("https://www.youtube.com/shorts/")], Now.UtcDateTime);
        await using var client = await ConnectAsync(options.PipeName);
        using var _ = await ReadJsonAsync(client);

        await SendAsync(client, "{not json");
        await SendAsync(
            client,
            """
            {"type":"blocked","url":"https://www.youtube.com/shorts/abc"}
            """);

        await WaitAsync(() =>
        {
            lock (attempts)
                return attempts.Count == 1;
        });
    }

    [Fact]
    public async Task Update_rules_broadcasts_a_fresh_snapshot_to_connected_hosts()
    {
        await using var bridge = CreateBridge(out var options);
        await bridge.StartAsync([DomainRule("chatgpt.com")], Now.UtcDateTime);
        await using var client = await ConnectAsync(options.PipeName);
        using var first = await ReadJsonAsync(client);
        first.RootElement.GetProperty("rules").GetArrayLength().Should().Be(1);

        await bridge.UpdateRulesAsync([], Now.AddMinutes(1).UtcDateTime);

        using var second = await ReadJsonAsync(client);
        second.RootElement.GetProperty("rules").GetArrayLength().Should().Be(0);
        second.RootElement.GetProperty("generatedAtUtc").GetDateTimeOffset()
            .Should().Be(Now.AddMinutes(1));
    }

    [Fact]
    public async Task Stop_marks_the_bridge_as_not_running()
    {
        await using var bridge = CreateBridge(out var options);
        await bridge.StartAsync([DomainRule("chatgpt.com")], Now.UtcDateTime);
        bridge.IsRunning.Should().BeTrue();

        await bridge.StopAsync();

        bridge.IsRunning.Should().BeFalse();
    }

    private static NamedPipeBrowserExtensionBridge CreateBridge(
        out BrowserExtensionBridgeOptions options)
    {
        options = new BrowserExtensionBridgeOptions
        {
            PipeName = "EduFocus.BrowserBridge.Test." + Guid.NewGuid().ToString("N")
        };

        return new NamedPipeBrowserExtensionBridge(
            options,
            new NullLogger(),
            new FixedTimeProvider(Now));
    }

    private static async Task<NamedPipeClientStream> ConnectAsync(string pipeName)
    {
        var client = new NamedPipeClientStream(
            ".",
            pipeName,
            PipeDirection.InOut,
            PipeOptions.Asynchronous);

        await client.ConnectAsync(5000);
        return client;
    }

    private static async Task SendAsync(NamedPipeClientStream client, string json)
    {
        var payload = Encoding.UTF8.GetBytes(json);
        var header = new byte[4];
        BinaryPrimitives.WriteUInt32LittleEndian(header, (uint)payload.Length);

        await client.WriteAsync(header);
        await client.WriteAsync(payload);
        await client.FlushAsync();
    }

    private static async Task<JsonDocument> ReadJsonAsync(NamedPipeClientStream client)
    {
        var header = new byte[4];
        await client.ReadExactlyAsync(header);
        var length = BinaryPrimitives.ReadUInt32LittleEndian(header);

        var payload = new byte[length];
        await client.ReadExactlyAsync(payload);

        return JsonDocument.Parse(payload);
    }

    private static async Task WaitAsync(Func<bool> condition)
    {
        var deadline = DateTime.UtcNow.AddSeconds(5);
        while (DateTime.UtcNow < deadline)
        {
            if (condition())
                return;

            await Task.Delay(10);
        }

        throw new TimeoutException("Condition was not met within 5 seconds.");
    }

    private static BlockRule DomainRule(string value) =>
        BlockRule.Create(
            RuleType.Domain,
            RuleMatchType.Exact,
            value,
            BlockRuleCategory.Chatbot,
            BlockRuleEffect.Block,
            Now.UtcDateTime.AddDays(-2),
            isEnabled: true,
            isBuiltIn: false);

    private static BlockRule UrlRule(string value) =>
        BlockRule.Create(
            RuleType.Url,
            RuleMatchType.Contains,
            value,
            BlockRuleCategory.Social,
            BlockRuleEffect.Block,
            Now.UtcDateTime.AddDays(-2),
            isEnabled: true,
            isBuiltIn: false);

    private sealed class NullLogger : IAppLogger
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

    private sealed class FixedTimeProvider(DateTimeOffset now) : TimeProvider
    {
        public override DateTimeOffset GetUtcNow() => now;

        public override TimeZoneInfo LocalTimeZone => TimeZoneInfo.Utc;
    }
}
