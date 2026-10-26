using System.Text;
using EduFocus.Domain.Entities;
using EduFocus.Domain.Enums;
using EduFocus.Infrastructure.Services;
using EduFocus.Ports.Outbound.Services;

namespace EduFocus.Infrastructure.Tests;

public sealed class WindowsHostsFilePortTests
{
    private static readonly DateTimeOffset Now = new(2026, 10, 26, 10, 0, 0, TimeSpan.Zero);

    [Fact]
    public async Task Apply_is_idempotent_and_restore_preserves_original_bytes_exactly()
    {
        var original = Encoding.UTF8.GetBytes("# custom host\r\n192.0.2.5 printer.local"); // no final newline
        var storage = new MemoryStorage();
        storage.Files["hosts"] = original;
        var dns = new FakeDns();
        var port = CreatePort(storage, dns);
        var rules = new[] { Rule("chatgpt.com") };
        Assert.True(rules[0].IsApplicable(Now));
        Assert.Equal(RuleEnforcementStatus.Enforced, rules[0].EnforcementStatus);
        Assert.Equal(BlockRuleType.Domain, rules[0].RuleType);
        Assert.Equal(BlockMatchType.Exact, rules[0].MatchType);
        Assert.Equal(UriHostNameType.Dns, Uri.CheckHostName(rules[0].NormalizedValue));

        await port.ApplyAsync(rules, Now);
        var firstApply = storage.Files["hosts"];
        await port.ApplyAsync(rules, Now.AddSeconds(30));
        var secondApply = storage.Files["hosts"];
        Assert.Equal(firstApply, secondApply);
        var activeText = Encoding.UTF8.GetString(secondApply);
        Assert.Contains("127.0.0.1\tchatgpt.com", activeText);
        Assert.Contains("::1\tchatgpt.com", activeText);
        Assert.Equal(1, Count(activeText, "127.0.0.1\tchatgpt.com"));
        Assert.Equal(1, Count(activeText, "::1\tchatgpt.com"));

        await port.RestoreAsync();
        Assert.Equal(original, storage.Files["hosts"]);
        Assert.False(storage.Files.ContainsKey("backup"));
        Assert.Equal(2, dns.FlushCount);
    }

    [Fact]
    public async Task Missing_hosts_file_is_created_for_protection_and_removed_on_restore()
    {
        var storage = new MemoryStorage();
        var port = CreatePort(storage, new FakeDns());
        await port.ApplyAsync(new[] { Rule("chatgpt.com") }, Now);
        Assert.True(storage.Files.ContainsKey("hosts"));
        Assert.True(storage.Files.ContainsKey("backup"));

        await port.RestoreAsync();

        Assert.False(storage.Files.ContainsKey("hosts"));
        Assert.False(storage.Files.ContainsKey("backup"));
    }

    [Fact]
    public async Task Only_enabled_unexpired_exact_domain_blocks_apply_and_allow_wins()
    {
        var storage = new MemoryStorage();
        storage.Files["hosts"] = Encoding.ASCII.GetBytes("127.0.0.1 localhost\n");
        var port = CreatePort(storage, new FakeDns());
        var rules = new[]
        {
            Rule("chatgpt.com"),
            Rule("meet.google.com"),
            Rule("disabled.example", enabled: false),
            Rule("expired.example", expiry: Now),
            Rule("suffix.example", match: BlockMatchType.Suffix),
            BlockRule.Create(BlockRuleType.Domain, BlockMatchType.Exact, "meet.google.com", BlockRuleCategory.Study, BlockRuleEffect.Allow, Now)
        };

        await port.ApplyAsync(rules, Now.AddSeconds(1));
        var text = Encoding.ASCII.GetString(storage.Files["hosts"]);
        Assert.Contains("chatgpt.com", text);
        Assert.DoesNotContain("meet.google.com", text);
        Assert.DoesNotContain("disabled.example", text);
        Assert.DoesNotContain("expired.example", text);
        Assert.DoesNotContain("suffix.example", text);
        Assert.DoesNotContain("chatgpt.com.evil.test", text);
    }

    [Fact]
    public async Task Empty_rules_do_not_touch_hosts_and_dns_failure_rolls_back_atomic_change()
    {
        var original = Encoding.ASCII.GetBytes("# user data\n");
        var storage = new MemoryStorage();
        storage.Files["hosts"] = original;
        var dns = new FakeDns { FailNext = true };
        var port = CreatePort(storage, dns);

        await port.ApplyAsync(Array.Empty<BlockRule>(), Now);
        Assert.Equal(original, storage.Files["hosts"]);
        await Assert.ThrowsAsync<InvalidOperationException>(() => port.ApplyAsync(new[] { Rule("chatgpt.com") }, Now));
        Assert.Equal(original, storage.Files["hosts"]);
        Assert.False(storage.Files.ContainsKey("backup"));
    }

    [Fact]
    public async Task Backup_or_hosts_access_denial_fails_without_claiming_success()
    {
        var original = Encoding.ASCII.GetBytes("# user's hosts\n");
        var storage = new MemoryStorage { DeniedWritePath = "hosts" };
        storage.Files["hosts"] = original;
        var logger = new FakeLogger();
        var port = new WindowsHostsFilePort("hosts", "backup", storage, new FakeDns(), logger);

        await Assert.ThrowsAsync<UnauthorizedAccessException>(() => port.ApplyAsync(new[] { Rule("chatgpt.com") }, Now));
        Assert.Equal(original, storage.Files["hosts"]);
        Assert.False(storage.Files.ContainsKey("backup"));
        Assert.DoesNotContain("user's hosts", logger.Messages);
        Assert.Contains("could not synchronize", logger.Messages.Single());
    }

    [Fact]
    public async Task Locked_hosts_file_is_left_untouched_and_error_does_not_include_hosts_contents()
    {
        var storage = new MemoryStorage { DeniedReadPath = "hosts" };
        var logger = new FakeLogger();
        var port = new WindowsHostsFilePort("hosts", "backup", storage, new FakeDns(), logger);

        await Assert.ThrowsAsync<IOException>(() => port.ApplyAsync(new[] { Rule("chatgpt.com") }, Now));

        Assert.Empty(storage.Files);
        Assert.DoesNotContain("chatgpt.com", string.Join(" ", logger.Messages));
    }

    [Fact]
    public async Task Restore_removes_only_managed_region_and_keeps_external_lines_added_outside_it()
    {
        var original = Encoding.ASCII.GetBytes("# original\n");
        var storage = new MemoryStorage();
        storage.Files["hosts"] = original;
        var port = CreatePort(storage, new FakeDns());
        await port.ApplyAsync(new[] { Rule("chatgpt.com") }, Now);
        storage.Files["hosts"] = Encoding.ASCII.GetBytes(Encoding.ASCII.GetString(storage.Files["hosts"]) + "# added by administrator\n");

        await port.RestoreAsync();

        Assert.Equal(Encoding.ASCII.GetString(original) + "# added by administrator\n", Encoding.ASCII.GetString(storage.Files["hosts"]));
    }

    [Fact]
    public async Task Damaged_marker_region_restores_original_backup()
    {
        var original = Encoding.ASCII.GetBytes("line1\nline2\n");
        var storage = new MemoryStorage();
        storage.Files["hosts"] = original;
        var port = CreatePort(storage, new FakeDns());
        await port.ApplyAsync(new[] { Rule("chatgpt.com") }, Now);
        storage.Files["hosts"] = Encoding.ASCII.GetBytes("# >>> EduFocus managed hosts BEGIN v1 >>>\npartial update\n");

        await port.RestoreAsync();

        Assert.Equal(original, storage.Files["hosts"]);
        Assert.False(storage.Files.ContainsKey("backup"));
    }

    private static WindowsHostsFilePort CreatePort(MemoryStorage storage, FakeDns dns) =>
        new("hosts", "backup", storage, dns, new FakeLogger());

    private static BlockRule Rule(string domain, bool enabled = true, DateTimeOffset? expiry = null, BlockMatchType match = BlockMatchType.Exact) =>
        BlockRule.Create(BlockRuleType.Domain, match, domain, BlockRuleCategory.Chatbot, BlockRuleEffect.Block, Now,
            enabled, expiresAtUtc: expiry);

    private static int Count(string value, string fragment) =>
        (value.Length - value.Replace(fragment, string.Empty, StringComparison.Ordinal).Length) / fragment.Length;

    private sealed class MemoryStorage : IHostsFileStorage
    {
        public Dictionary<string, byte[]> Files { get; } = new(StringComparer.Ordinal);
        public string? DeniedWritePath { get; init; }
        public string? DeniedReadPath { get; init; }
        public Task<byte[]?> ReadAsync(string path, CancellationToken cancellationToken)
        {
            if (Path.GetFileName(path) == DeniedReadPath) throw new IOException("simulated file lock");
            return Task.FromResult(Files.TryGetValue(Path.GetFileName(path), out var bytes) ? bytes.ToArray() : null);
        }
        public Task WriteAtomicallyAsync(string path, byte[] contents, CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (Path.GetFileName(path) == DeniedWritePath) throw new UnauthorizedAccessException("simulated access denial");
            Files[Path.GetFileName(path)] = contents.ToArray();
            return Task.CompletedTask;
        }
        public Task DeleteAsync(string path, CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();
            Files.Remove(Path.GetFileName(path));
            return Task.CompletedTask;
        }
    }

    private sealed class FakeDns : IDnsCacheFlusher
    {
        public int FlushCount { get; private set; }
        public bool FailNext { get; set; }
        public Task FlushAsync(CancellationToken cancellationToken)
        {
            FlushCount++;
            if (FailNext) { FailNext = false; throw new InvalidOperationException("simulated DNS flush failure"); }
            return Task.CompletedTask;
        }
    }

    private sealed class FakeLogger : IAppLogger
    {
        public List<string> Messages { get; } = [];
        public void Info(string message) => Messages.Add(message);
        public void Warning(string message) => Messages.Add(message);
        public void Error(string message, Exception? ex = null) => Messages.Add(message);
    }
}
