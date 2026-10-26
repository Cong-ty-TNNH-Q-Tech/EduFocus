using System.Collections;
using System.Globalization;
using System.Runtime.CompilerServices;
using System.Runtime.Versioning;
using System.Text.Json;
using System.Text.RegularExpressions;
using EduFocus.Ports.Outbound.Services;

namespace EduFocus.Infrastructure.Logging;

public sealed record FileLoggerOptions
{
    public required string DirectoryPath { get; init; }
    public long MaxFileBytes { get; init; } = 5 * 1024 * 1024;
    public int RetentionDays { get; init; } = 14;
    public int MaxFiles { get; init; } = 112;
}

[SupportedOSPlatform("windows")]
public sealed class FileLogger : IAppLogger, IDisposable
{
    private static readonly AsyncLocal<Guid?> AmbientCorrelation = new();
    private static readonly Regex Url = new("(?i)\\b(?:https?://|ftp://)[^\\s\\]<>\\\"']+", RegexOptions.Compiled | RegexOptions.CultureInvariant);
    private static readonly Regex SensitiveAssignment = new("(?i)\\b(pin|recovery(?:\\s*code)?|token|access[_ -]?token|refresh[_ -]?token|password|secret|authorization)\\s*[:=]\\s*(?:Bearer\\s+)?(?:\\\"[^\\\"]*\\\"|'[^']*'|[^\\s,;]+)", RegexOptions.Compiled | RegexOptions.CultureInvariant);
    private static readonly Regex Bearer = new(@"(?i)\bBearer\s+[A-Za-z0-9._~+/-]+=*", RegexOptions.Compiled | RegexOptions.CultureInvariant);
    private readonly FileLoggerOptions _options;
    private readonly object _lock = new();

    public FileLogger() : this(new FileLoggerOptions
    {
        DirectoryPath = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.CommonApplicationData), "EduFocus", "logs")
    }) { }

    public FileLogger(FileLoggerOptions options)
    {
        _options = options ?? throw new ArgumentNullException(nameof(options));
        if (string.IsNullOrWhiteSpace(options.DirectoryPath)) throw new ArgumentException("Log directory is required.", nameof(options));
        if (options.MaxFileBytes < 512) throw new ArgumentOutOfRangeException(nameof(options), "Max file size must be at least 512 bytes.");
        if (options.RetentionDays < 1 || options.MaxFiles < 1) throw new ArgumentOutOfRangeException(nameof(options));
        Directory.CreateDirectory(options.DirectoryPath);
        lock (_lock) PurgeOldLogs(DateTimeOffset.UtcNow);
    }

    public void Info(string message) => Write("Information", "EduFocus", message, null, null);
    public void Warning(string message) => Write("Warning", "EduFocus", message, null, null);
    public void Error(string message, Exception? ex = null) => Write("Error", "EduFocus", message, ex, null);

    public void Log(string level, string category, string message, Exception? exception = null, IReadOnlyDictionary<string, object?>? properties = null)
    {
        if (string.IsNullOrWhiteSpace(level)) throw new ArgumentException("Level is required.", nameof(level));
        if (string.IsNullOrWhiteSpace(category)) throw new ArgumentException("Category is required.", nameof(category));
        Write(level, category, message, exception, properties);
    }

    public IDisposable BeginCorrelationScope(Guid? correlationId = null)
    {
        var previous = AmbientCorrelation.Value;
        var current = correlationId ?? Guid.NewGuid();
        if (current == Guid.Empty) throw new ArgumentException("Correlation ID cannot be empty.", nameof(correlationId));
        AmbientCorrelation.Value = current;
        return new Scope(previous);
    }

    public static string Redact(string? value)
    {
        if (string.IsNullOrEmpty(value)) return string.Empty;
        var sanitized = SensitiveAssignment.Replace(value, m => $"{m.Groups[1].Value}=[REDACTED]");
        sanitized = Bearer.Replace(sanitized, "Bearer [REDACTED]");
        sanitized = Url.Replace(sanitized, "[URL REDACTED]");
        return sanitized.Length <= 4096 ? sanitized : sanitized[..4096] + "…";
    }

    private void Write(string level, string category, string message, Exception? exception, IReadOnlyDictionary<string, object?>? properties)
    {
        var now = DateTimeOffset.UtcNow;
        var entry = new Dictionary<string, object?>(StringComparer.Ordinal)
        {
            ["timestampUtc"] = now.UtcDateTime.ToString("O", CultureInfo.InvariantCulture),
            ["level"] = Redact(level),
            ["category"] = Redact(category),
            ["correlationId"] = (AmbientCorrelation.Value ?? Guid.NewGuid()).ToString("D"),
            ["message"] = Redact(message)
        };
        if (exception is not null)
        {
            entry["exceptionType"] = exception.GetType().FullName;
            entry["exceptionMessage"] = Redact(exception.Message);
            // Avoid raw exception.ToString(): custom exception text and data may contain secrets.
        }
        if (properties is not null)
        {
            var safe = new Dictionary<string, object?>(StringComparer.Ordinal);
            foreach (var (key, value) in properties) safe[Redact(key)] = SanitizeValue(key, value);
            entry["properties"] = safe;
        }

        string line;
        try { line = JsonSerializer.Serialize(entry) + "\n"; }
        catch { return; }
        var bytes = System.Text.Encoding.UTF8.GetBytes(line);
        lock (_lock)
        {
            try
            {
                var path = CurrentPath(now);
                if (File.Exists(path) && new FileInfo(path).Length + bytes.Length > _options.MaxFileBytes)
                    path = NextPath(now);
                if (bytes.Length <= _options.MaxFileBytes) File.AppendAllText(path, line, System.Text.Encoding.UTF8);
                PurgeOldLogs(now);
            }
            catch { /* Logging must never crash the application. */ }
        }
    }

    private static object? SanitizeValue(string key, object? value)
    {
        if (IsSensitiveKey(key)) return "[REDACTED]";
        if (value is null) return null;
        if (value is string text) return Redact(text);
        if (value is Exception ex) return new { type = ex.GetType().FullName, message = Redact(ex.Message) };
        if (value is IDictionary dictionary)
        {
            var result = new Dictionary<string, object?>();
            foreach (DictionaryEntry item in dictionary)
                if (item.Key is string name) result[name] = SanitizeValue(name, item.Value);
            return result;
        }
        if (value is IEnumerable items)
        {
            var result = new List<object?>();
            foreach (var item in items) result.Add(item is string s ? Redact(s) : SanitizeValue(key, item));
            return result;
        }
        return value.GetType().IsPrimitive || value is decimal || value is Guid || value is DateTime || value is DateTimeOffset
            ? value : "[OMITTED]";
    }

    private static bool IsSensitiveKey(string key)
    {
        var k = key.Replace("_", "", StringComparison.Ordinal).Replace("-", "", StringComparison.Ordinal).ToLowerInvariant();
        return k.Contains("pin") || k.Contains("recovery") || k.Contains("token") || k.Contains("password") || k.Contains("secret") ||
               k.Contains("authorization") || k.Contains("url") || k.Contains("windowtitle") || k.Contains("pagetitle") ||
               k.Contains("pagecontent") || k.Contains("camera") || k.Contains("frame") || k.Contains("image") || k.Contains("video") || k.Contains("audio");
    }

    private string CurrentPath(DateTimeOffset now) => Path.Combine(_options.DirectoryPath, $"app-{now:yyyyMMdd}-000.jsonl");

    private string NextPath(DateTimeOffset now)
    {
        var index = 0;
        while (index < _options.MaxFiles && File.Exists(Path.Combine(_options.DirectoryPath, $"app-{now:yyyyMMdd}-{index:000}.jsonl"))) index++;
        if (index >= _options.MaxFiles)
        {
            // Keep the per-day shard count bounded while still rotating instead of growing a full shard.
            var oldest = Enumerable.Range(0, _options.MaxFiles)
                .Select(i => Path.Combine(_options.DirectoryPath, $"app-{now:yyyyMMdd}-{i:000}.jsonl"))
                .Where(File.Exists)
                .OrderBy(File.GetLastWriteTimeUtc)
                .FirstOrDefault();
            if (oldest is null) index = 0;
            else
            {
                index = int.Parse(Path.GetFileNameWithoutExtension(oldest)[^3..], CultureInfo.InvariantCulture);
                File.Delete(oldest);
            }
        }
        return Path.Combine(_options.DirectoryPath, $"app-{now:yyyyMMdd}-{index:000}.jsonl");
    }

    private void PurgeOldLogs(DateTimeOffset now)
    {
        try
        {
            var files = Directory.GetFiles(_options.DirectoryPath, "app-*.jsonl")
                .Select(path => new FileInfo(path)).OrderByDescending(file => file.LastWriteTimeUtc).ToArray();
            var cutoff = now.UtcDateTime.AddDays(-_options.RetentionDays);
            foreach (var file in files.Where(file => file.LastWriteTimeUtc < cutoff).Concat(files.Skip(_options.MaxFiles)).DistinctBy(file => file.FullName))
                file.Delete();
        }
        catch { /* Retention is best effort; logging remains available. */ }
    }

    public void Dispose() { }

    private sealed class Scope(Guid? previous) : IDisposable
    {
        private Guid? _previous = previous;
        private bool _disposed;
        public void Dispose()
        {
            if (_disposed) return;
            AmbientCorrelation.Value = _previous;
            _previous = null;
            _disposed = true;
        }
    }
}
