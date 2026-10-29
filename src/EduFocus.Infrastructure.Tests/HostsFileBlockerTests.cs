using System.Text;
using EduFocus.Infrastructure.Protection;
using EduFocus.Ports.Outbound.Services;
using EduFocus.Ports.Protection;
using FluentAssertions;

namespace EduFocus.Infrastructure.Tests;

public sealed class HostsFileBlockerTests : IDisposable
{
    private const string SampleHosts = "# sample hosts\r\n# comment\r\n127.0.0.1 localhost\r\n";

    private readonly string _root;
    private readonly string _hostsPath;
    private readonly string _backupDirectory;
    private readonly FakeDnsFlusher _dns = new();
    private readonly RecordingLogger _logger = new();

    public HostsFileBlockerTests()
    {
        _root = Path.Combine(Path.GetTempPath(), "edufocus-hosts-tests", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(_root);
        _hostsPath = Path.Combine(_root, "hosts");
        _backupDirectory = Path.Combine(_root, "backup");
    }

    [Fact]
    public async Task Apply_writes_marker_section_with_ipv4_and_ipv6_entries()
    {
        WriteHosts(SampleHosts);
        var originalBytes = ReadHosts();

        var result = await CreateBlocker()
            .ApplyAsync(["Example.COM.", "chatgpt.com", "claude.ai", "chatgpt.com"]);

        var text = ReadHostsText();
        result.Changed.Should().BeTrue();
        result.DomainCount.Should().Be(3);
        result.Domains.Should().Equal("chatgpt.com", "claude.ai", "example.com");
        File.ReadAllBytes(result.BackupPath).Should().Equal(originalBytes);

        text.Should().Contain("### EDUFOCUS BLOCK START ###");
        text.Should().Contain("### EDUFOCUS BLOCK END ###");
        text.Should().Contain("127.0.0.1 chatgpt.com\r\n");
        text.Should().Contain("::1 chatgpt.com\r\n");
        text.Should().Contain("127.0.0.1 claude.ai\r\n");
        text.Should().Contain("::1 claude.ai\r\n");
        text.Should().Contain("127.0.0.1 example.com\r\n");
        text.Should().Contain("# sample hosts");
        text.Should().Contain("127.0.0.1 localhost");

        text.Split(["127.0.0.1 chatgpt.com"], StringSplitOptions.None).Should().HaveCount(2);
        _dns.FlushCount.Should().Be(1);
    }

    [Fact]
    public async Task Apply_replaces_previous_entries_when_rules_change()
    {
        WriteHosts(SampleHosts);
        var blocker = CreateBlocker();

        await blocker.ApplyAsync(["chatgpt.com", "claude.ai"]);
        var result = await blocker.ApplyAsync(["claude.ai"]);

        var text = ReadHostsText();
        result.Changed.Should().BeTrue();
        result.DomainCount.Should().Be(1);
        text.Should().NotContain("chatgpt.com");
        text.Should().Contain("127.0.0.1 claude.ai");
        _dns.FlushCount.Should().Be(2);
    }

    [Fact]
    public async Task Apply_is_idempotent()
    {
        WriteHosts(SampleHosts);
        var blocker = CreateBlocker();

        var first = await blocker.ApplyAsync(["example.com"]);
        var firstBytes = ReadHosts();
        var second = await blocker.ApplyAsync(["example.com"]);

        first.Changed.Should().BeTrue();
        second.Changed.Should().BeFalse();
        ReadHosts().Should().Equal(firstBytes);
        _dns.FlushCount.Should().Be(1);
    }

    [Theory]
    [InlineData(SampleHosts)]
    [InlineData("127.0.0.1 localhost")]
    public async Task Restore_returns_the_original_bytes(string original)
    {
        WriteHosts(original);
        var originalBytes = ReadHosts();
        var blocker = CreateBlocker();

        await blocker.ApplyAsync(["example.com"]);
        var restore = await blocker.RestoreAsync();

        restore.BlockRemoved.Should().BeTrue();
        restore.RestoredExact.Should().BeTrue();
        ReadHosts().Should().Equal(originalBytes);
        ReadHostsText().Should().NotContain("EDUFOCUS BLOCK");
        _dns.FlushCount.Should().Be(2);
    }

    [Fact]
    public async Task Restore_keeps_edits_made_outside_the_block_section()
    {
        WriteHosts(SampleHosts);
        var blocker = CreateBlocker();
        await blocker.ApplyAsync(["example.com"]);

        File.AppendAllText(_hostsPath, "10.0.0.5 manual.local\r\n", Encoding.Latin1);
        var restore = await blocker.RestoreAsync();

        var text = ReadHostsText();
        restore.BlockRemoved.Should().BeTrue();
        restore.RestoredExact.Should().BeFalse();
        text.Should().NotContain("EDUFOCUS BLOCK");
        text.Should().Contain("10.0.0.5 manual.local");
        text.Should().Contain("# sample hosts");
        text.Should().Contain("127.0.0.1 localhost");
    }

    [Fact]
    public async Task Restore_without_a_block_section_is_a_noop()
    {
        WriteHosts(SampleHosts);
        var before = ReadHosts();

        var restore = await CreateBlocker().RestoreAsync();

        restore.BlockRemoved.Should().BeFalse();
        restore.RestoredExact.Should().BeTrue();
        ReadHosts().Should().Equal(before);
        _dns.FlushCount.Should().Be(0);
    }

    [Fact]
    public async Task SelfHeal_removes_an_orphan_block_section()
    {
        var orphan = SampleHosts + HostsFileBlocker.StartMarker + "\r\n::1 example.com\r\n";
        WriteHosts(orphan);

        var result = await CreateBlocker().SelfHealAsync();

        var text = ReadHostsText();
        result.Performed.Should().BeTrue();
        result.FromJournal.Should().BeFalse();
        text.Should().NotContain("EDUFOCUS BLOCK");
        text.Should().Contain("# sample hosts");
        text.Should().Contain("127.0.0.1 localhost");
        _dns.FlushCount.Should().Be(1);
    }

    [Fact]
    public async Task SelfHeal_restores_the_original_hosts_after_a_crash()
    {
        WriteHosts(SampleHosts);
        var originalBytes = ReadHosts();
        await CreateBlocker().ApplyAsync(["example.com"]);

        var result = await CreateBlocker().SelfHealAsync();

        result.Performed.Should().BeTrue();
        result.FromJournal.Should().BeTrue();
        ReadHosts().Should().Equal(originalBytes);
        File.Exists(Path.Combine(_backupDirectory, "journal.json")).Should().BeFalse();
        _dns.FlushCount.Should().Be(2);
    }

    [Fact]
    public async Task SelfHeal_without_markers_or_journal_is_a_noop()
    {
        WriteHosts(SampleHosts);
        var before = ReadHosts();

        var result = await CreateBlocker().SelfHealAsync();

        result.Performed.Should().BeFalse();
        result.FromJournal.Should().BeFalse();
        ReadHosts().Should().Equal(before);
        _dns.FlushCount.Should().Be(0);
    }

    [Theory]
    [InlineData("http://example.com")]
    [InlineData("*.example.com")]
    [InlineData("example.com/path")]
    [InlineData("exa mple.com")]
    [InlineData("example.com:80")]
    [InlineData("localhost")]
    [InlineData("127.0.0.1")]
    [InlineData("")]
    public async Task Apply_rejects_invalid_domains_without_touching_hosts(string domain)
    {
        WriteHosts(SampleHosts);
        var before = ReadHosts();

        var act = () => CreateBlocker().ApplyAsync([domain]);

        await act.Should().ThrowAsync<ArgumentException>();
        ReadHosts().Should().Equal(before);
        _dns.FlushCount.Should().Be(0);
    }

    [Fact]
    public async Task Apply_reports_a_missing_hosts_file()
    {
        var act = () => CreateBlocker().ApplyAsync(["example.com"]);

        var exception = (await act.Should().ThrowAsync<HostsFileException>()).Which;
        exception.Operation.Should().Be("apply");
        exception.Reason.Should().Be(HostsFileFailureReason.NotFound);
    }

    [Fact]
    public async Task Apply_reports_access_denied_when_hosts_path_is_a_directory()
    {
        Directory.CreateDirectory(_hostsPath);

        var act = () => CreateBlocker().ApplyAsync(["example.com"]);

        var exception = (await act.Should().ThrowAsync<HostsFileException>()).Which;
        exception.Reason.Should().Be(HostsFileFailureReason.AccessDenied);
    }

    [Fact]
    public async Task Apply_reports_a_read_only_hosts_file_as_access_denied()
    {
        WriteHosts(SampleHosts);
        File.SetAttributes(_hostsPath, FileAttributes.ReadOnly);
        try
        {
            var act = () => CreateBlocker().ApplyAsync(["example.com"]);

            var exception = (await act.Should().ThrowAsync<HostsFileException>()).Which;
            exception.Operation.Should().Be("apply");
            exception.Reason.Should().Be(HostsFileFailureReason.AccessDenied);
        }
        finally
        {
            File.SetAttributes(_hostsPath, FileAttributes.Normal);
        }
    }

    [Fact]
    public async Task Apply_reports_locked_when_the_hosts_file_is_held_open()
    {
        WriteHosts(SampleHosts);
        using var locked = new FileStream(_hostsPath, FileMode.Open, FileAccess.Read, FileShare.Read);
        var options = DefaultOptions() with
        {
            WriteAttempts = 2,
            WriteRetryDelay = TimeSpan.FromMilliseconds(10)
        };

        var act = () => CreateBlocker(options).ApplyAsync(["example.com"]);

        var exception = (await act.Should().ThrowAsync<HostsFileException>()).Which;
        exception.Operation.Should().Be("apply");
        exception.Reason.Should().Be(HostsFileFailureReason.Locked);
    }

    [Fact]
    public async Task Apply_succeeds_when_the_dns_flush_fails()
    {
        WriteHosts(SampleHosts);
        _dns.Failure = new InvalidOperationException("no dns");

        var result = await CreateBlocker().ApplyAsync(["example.com"]);

        result.Changed.Should().BeTrue();
        ReadHostsText().Should().Contain("127.0.0.1 example.com");
        _logger.Warnings.Should().Contain(message => message.Contains("flush the DNS cache"));
    }

    [Fact]
    public async Task Apply_logs_counts_but_not_domain_values()
    {
        WriteHosts(SampleHosts);

        await CreateBlocker().ApplyAsync(["chatgpt.com"]);

        _logger.Messages.Should().Contain(message => message.Contains("1 domains"));
        _logger.Messages.Should().NotContain(message => message.Contains("chatgpt.com"));
    }

    public void Dispose()
    {
        try
        {
            if (Directory.Exists(_root))
                Directory.Delete(_root, recursive: true);
        }
        catch (IOException)
        {
        }
        catch (UnauthorizedAccessException)
        {
        }
    }

    private HostsFileBlocker CreateBlocker(HostsFileOptions? options = null) =>
        new(options ?? DefaultOptions(), _dns, _logger);

    private HostsFileOptions DefaultOptions() =>
        new()
        {
            HostsPath = _hostsPath,
            BackupDirectory = _backupDirectory,
            WriteAttempts = 3,
            WriteRetryDelay = TimeSpan.FromMilliseconds(5)
        };

    private void WriteHosts(string text) =>
        File.WriteAllBytes(_hostsPath, Encoding.Latin1.GetBytes(text));

    private byte[] ReadHosts() => File.ReadAllBytes(_hostsPath);

    private string ReadHostsText() => Encoding.Latin1.GetString(ReadHosts());

    private sealed class FakeDnsFlusher : IDnsFlusher
    {
        public int FlushCount { get; private set; }

        public Exception? Failure { get; set; }

        public Task FlushAsync(CancellationToken cancellationToken = default)
        {
            FlushCount++;
            return Failure is null ? Task.CompletedTask : Task.FromException(Failure);
        }
    }

    private sealed class RecordingLogger : IAppLogger
    {
        public List<string> Messages { get; } = [];

        public List<string> Warnings { get; } = [];

        public void Info(string category, string message) => Messages.Add(message);

        public void Warning(string category, string message)
        {
            Messages.Add(message);
            Warnings.Add(message);
        }

        public void Error(string category, string message, Exception? exception = null) =>
            Messages.Add(message);
    }
}
