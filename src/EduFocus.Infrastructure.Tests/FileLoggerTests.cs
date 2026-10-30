using System.Globalization;
using EduFocus.Infrastructure.Logging;
using EduFocus.Ports.Outbound.Services;
using FluentAssertions;

namespace EduFocus.Infrastructure.Tests;

public sealed class FileLoggerTests : IDisposable
{
    private readonly string _root;
    private readonly CorrelationContext _correlation = new();

    public FileLoggerTests()
    {
        _root = Path.Combine(Path.GetTempPath(), "edufocus-log-tests", Guid.NewGuid().ToString("N"));
    }

    [Fact]
    public void Write_includes_utc_timestamp_category_and_correlation_id()
    {
        var correlationId = Guid.NewGuid();
        using var scope = _correlation.BeginScope(correlationId);
        var logger = CreateLogger();

        logger.Info(LogCategories.Database, "Database ready");

        var line = ReadLogLines().Single();
        line.Should().MatchRegex(
            $@"^\d{{4}}-\d{{2}}-\d{{2}}T\d{{2}}:\d{{2}}:\d{{2}}\.\d{{3}}Z \[INFO \] \[Database\] \[cid={correlationId:N}\] Database ready$");

        DateTimeOffset.Parse(line[..24], CultureInfo.InvariantCulture, DateTimeStyles.AssumeUniversal)
            .Should()
            .BeCloseTo(DateTimeOffset.UtcNow, TimeSpan.FromSeconds(5));
    }

    [Fact]
    public void Write_uses_dash_when_no_correlation_scope_is_active()
    {
        var logger = CreateLogger();

        logger.Warning(LogCategories.App, "No scope");

        ReadLogLines().Single().Should().Contain("[WARN ] [App] [cid=-] No scope");
    }

    [Fact]
    public void Write_redacts_secrets_urls_and_exception_values()
    {
        var logger = CreateLogger();

        logger.Error(
            LogCategories.Security,
            "PIN=9999 blocked https://example.com/class/7?token=abc",
            new InvalidOperationException("token=secret-value"));

        var content = string.Join(Environment.NewLine, ReadLogLines());

        content.Should().Contain("[ERROR]").And.Contain("[Security]");
        content.Should().Contain("[url:example.com]");
        content.Should().Contain("PIN=***").And.NotContain("9999");
        content.Should().NotContain("token=abc").And.NotContain("token=secret-value");
        content.Should().Contain("System.InvalidOperationException");
    }

    [Fact]
    public void Write_redacts_whitespace_secrets_nested_exceptions_and_url_details()
    {
        var logger = CreateLogger();

        logger.Error(
            LogCategories.Security,
            "Parent PIN 482915 rejected; recovery code is ABCDEFGHJKLM; see https://example.com/reset?token=abc#frag",
            new InvalidOperationException(
                "outer token=secret-value",
                new Exception("password: hunter2")));

        var content = string.Join(Environment.NewLine, ReadLogLines());

        content.Should().Contain("Parent PIN=***");
        content.Should().Contain("recovery code=***");
        content.Should().Contain("[url:example.com]");
        content.Should().NotContainAny(
            "482915",
            "ABCDEFGHJKLM",
            "token=abc",
            "secret-value",
            "hunter2",
            "reset",
            "frag");
        content.Should().Contain("outer token=***");
        content.Should().Contain("password=***");
    }

    [Fact]
    public void Write_rolls_files_when_size_limit_is_reached()
    {
        var logger = CreateLogger(fileSizeLimitBytes: 4096);

        logger.Info(LogCategories.App, new string('a', 5000));
        logger.Info(LogCategories.App, new string('b', 5000));
        logger.Info(LogCategories.App, new string('c', 5000));

        var files = Directory.GetFiles(_root, "edufocus-*.log")
            .OrderBy(path => path, StringComparer.OrdinalIgnoreCase)
            .ToList();

        files.Should().HaveCountGreaterThanOrEqualTo(2);

        var content = string.Join(
            Environment.NewLine,
            files.Select(File.ReadAllText));

        content.Should()
            .Contain(new string('a', 100))
            .And.Contain(new string('b', 100))
            .And.Contain(new string('c', 100));
    }

    [Fact]
    public void Write_purges_logs_older_than_retention_window()
    {
        Directory.CreateDirectory(_root);
        var today = DateTime.Now.Date;
        var oldPath = Path.Combine(_root, $"edufocus-{today.AddDays(-20):yyyyMMdd}.log");
        var recentPath = Path.Combine(_root, $"edufocus-{today.AddDays(-2):yyyyMMdd}.log");
        File.WriteAllText(oldPath, "old");
        File.WriteAllText(recentPath, "recent");

        var logger = CreateLogger();
        logger.Info(LogCategories.App, "started");

        File.Exists(oldPath).Should().BeFalse();
        File.Exists(recentPath).Should().BeTrue();
    }

    private FileLogger CreateLogger(long fileSizeLimitBytes = FileLoggerOptions.DefaultFileSizeLimitBytes) =>
        new(
            new FileLoggerOptions
            {
                DirectoryPath = _root,
                FileSizeLimitBytes = fileSizeLimitBytes
            },
            _correlation);

    private string[] ReadLogLines() =>
        Directory.GetFiles(_root, "edufocus-*.log")
            .OrderBy(path => path, StringComparer.OrdinalIgnoreCase)
            .SelectMany(File.ReadAllLines)
            .ToArray();

    public void Dispose()
    {
        try
        {
            Directory.Delete(_root, recursive: true);
        }
        catch (IOException)
        {
        }
        catch (UnauthorizedAccessException)
        {
        }
    }
}
