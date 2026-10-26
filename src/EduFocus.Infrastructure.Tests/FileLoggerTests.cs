using System.Text.Json;
using EduFocus.Infrastructure.Logging;

namespace EduFocus.Infrastructure.Tests;

public sealed class FileLoggerTests
{
    [Fact]
    public void WritesUtcStructuredRecordWithCorrelationAndRedactsSensitiveContent()
    {
        var dir = NewDirectory();
        try
        {
            var correlation = Guid.NewGuid();
            using var logger = NewLogger(dir);
            using (logger.BeginCorrelationScope(correlation))
            {
                logger.Log("Warning", "Protection.Engine", "PIN=123456 token=abc https://example.test/private?q=secret", properties: new Dictionary<string, object?>
                {
                    ["windowTitle"] = "Private document - child name",
                    ["cameraFrame"] = "frame-data",
                    ["count"] = 3
                });
            }

            using var doc = JsonDocument.Parse(File.ReadAllText(Directory.GetFiles(dir, "*.jsonl").Single()));
            var root = doc.RootElement;
            Assert.Equal("Warning", root.GetProperty("level").GetString());
            Assert.Equal("Protection.Engine", root.GetProperty("category").GetString());
            Assert.Equal(correlation.ToString("D"), root.GetProperty("correlationId").GetString());
            Assert.EndsWith("Z", root.GetProperty("timestampUtc").GetString());
            var line = root.GetRawText();
            Assert.DoesNotContain("123456", line);
            Assert.DoesNotContain("abc", line);
            Assert.DoesNotContain("example.test", line);
            Assert.DoesNotContain("Private document", line);
            Assert.DoesNotContain("frame-data", line);
            Assert.Equal("3", root.GetProperty("properties").GetProperty("count").GetRawText());
        }
        finally { Directory.Delete(dir, true); }
    }

    [Fact]
    public void RedactsSensitiveAssignmentsBearerTokensAndUrlsInMessages()
    {
        var safe = FileLogger.Redact("recovery code=R-123 token=secret Authorization: Bearer xyz https://example.test/a");
        Assert.DoesNotContain("R-123", safe);
        Assert.DoesNotContain("secret", safe);
        Assert.DoesNotContain("xyz", safe);
        Assert.DoesNotContain("example.test", safe);
    }

    [Fact]
    public void CorrelationScopesRestoreTheParentAndUnscopedRecordsStillGetAnId()
    {
        var dir = NewDirectory();
        try
        {
            using var logger = NewLogger(dir);
            var parent = Guid.NewGuid();
            using (logger.BeginCorrelationScope(parent))
            {
                logger.Info("parent");
                using (logger.BeginCorrelationScope()) logger.Info("child");
                logger.Info("parent again");
            }
            logger.Info("standalone");

            var records = Directory.GetFiles(dir, "*.jsonl").SelectMany(path => File.ReadAllLines(path)).Select(line => JsonDocument.Parse(line)).ToArray();
            try
            {
                Assert.Equal(parent.ToString("D"), records[0].RootElement.GetProperty("correlationId").GetString());
                Assert.NotEqual(parent.ToString("D"), records[1].RootElement.GetProperty("correlationId").GetString());
                Assert.Equal(parent.ToString("D"), records[2].RootElement.GetProperty("correlationId").GetString());
                Assert.False(string.IsNullOrWhiteSpace(records[3].RootElement.GetProperty("correlationId").GetString()));
            }
            finally { foreach (var record in records) record.Dispose(); }
        }
        finally { Directory.Delete(dir, true); }
    }

    [Fact]
    public void RollsFilesAtConfiguredSizeAndPurgesExpiredFiles()
    {
        var dir = NewDirectory();
        try
        {
            File.WriteAllText(Path.Combine(dir, "app-20200101-000.jsonl"), "old");
            File.SetLastWriteTimeUtc(Path.Combine(dir, "app-20200101-000.jsonl"), DateTime.UtcNow.AddDays(-30));
            using var logger = NewLogger(dir, maxFileBytes: 512);
            logger.Info(new string('x', 300));
            logger.Info(new string('y', 300));
            Assert.False(File.Exists(Path.Combine(dir, "app-20200101-000.jsonl")));
            Assert.True(Directory.GetFiles(dir, "*.jsonl").Length >= 2);
            Assert.All(Directory.GetFiles(dir, "*.jsonl"), path => Assert.True(new FileInfo(path).Length <= 512));
        }
        finally { Directory.Delete(dir, true); }
    }

    private static FileLogger NewLogger(string dir, long maxFileBytes = 5 * 1024 * 1024) => new(new FileLoggerOptions
    {
        DirectoryPath = dir, MaxFileBytes = maxFileBytes, RetentionDays = 14, MaxFiles = 112
    });

    private static string NewDirectory()
    {
        var path = Path.Combine(Path.GetTempPath(), "EduFocus-logger-tests", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(path);
        return path;
    }
}
