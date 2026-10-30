using System.Globalization;
using System.Text;
using System.Text.RegularExpressions;
using EduFocus.Domain.Privacy;
using EduFocus.Ports.Outbound.Services;

namespace EduFocus.Infrastructure.Logging;

/// <summary>
/// Append-only technical file log with one file per local day, size-based
/// rolling, retention and secret redaction. Each line starts with an UTC
/// timestamp, level, category and the ambient correlation id. Writes are
/// best-effort and never throw (system architecture §12).
/// </summary>
public sealed class FileLogger : IAppLogger
{
    public const string FilePrefix = "edufocus";
    private const string FileExtension = ".log";
    private const string TimestampFormat = "yyyy-MM-dd'T'HH:mm:ss.fff'Z'";
    private const int MaxCategoryLength = 32;

    private static readonly Regex FileNamePattern = new(
        @"^edufocus-(?<date>\d{8})(?:-\d{3,})?\.log$",
        RegexOptions.IgnoreCase | RegexOptions.CultureInvariant | RegexOptions.NonBacktracking);

    private readonly FileLoggerOptions _options;
    private readonly ICorrelationContext _correlation;
    private readonly TimeProvider _timeProvider;
    private readonly object _lock = new();
    private DateOnly? _lastPurgeDate;
    private bool _directoryReady;

    public FileLogger(
        FileLoggerOptions options,
        ICorrelationContext correlation,
        TimeProvider? timeProvider = null)
    {
        ArgumentNullException.ThrowIfNull(options);
        ArgumentNullException.ThrowIfNull(correlation);

        if (string.IsNullOrWhiteSpace(options.DirectoryPath))
            throw new ArgumentException("A log directory is required.", nameof(options));
        if (options.RetentionDays is < 1 or > 90)
            throw new ArgumentOutOfRangeException(nameof(options), "Retention days must be between 1 and 90.");
        if (options.FileSizeLimitBytes < 4096)
            throw new ArgumentOutOfRangeException(nameof(options), "The log file size limit must be at least 4 KB.");

        _options = options;
        _correlation = correlation;
        _timeProvider = timeProvider ?? TimeProvider.System;
    }

    public void Info(string category, string message) =>
        Write("INFO", category, message, null);

    public void Warning(string category, string message) =>
        Write("WARN", category, message, null);

    public void Error(string category, string message, Exception? exception = null) =>
        Write("ERROR", category, message, exception);

    private void Write(string level, string category, string message, Exception? exception)
    {
        try
        {
            var safeMessage = LogSanitizer.Redact(message);
            var line = new StringBuilder()
                .Append(_timeProvider.GetUtcNow().UtcDateTime.ToString(TimestampFormat, CultureInfo.InvariantCulture))
                .Append(" [").Append(level.PadRight(5)).Append(']')
                .Append(" [").Append(NormalizeCategory(category)).Append(']')
                .Append(" [cid=").Append(_correlation.Current?.ToString("N") ?? "-").Append("] ")
                .Append(safeMessage);

            if (exception is not null)
            {
                line.Append(Environment.NewLine)
                    .Append(LogSanitizer.RedactPreservingLines(exception.ToString()));
            }

            var entry = line.Append(Environment.NewLine).ToString();

            lock (_lock)
            {
                EnsureDirectoryAndPurge();
                File.AppendAllText(SelectCurrentPath(), entry, Encoding.UTF8);
            }
        }
        catch
        {
            // Logging must never take the application down.
        }
    }

    private string SelectCurrentPath()
    {
        var date = _timeProvider.GetLocalNow().ToString("yyyyMMdd", CultureInfo.InvariantCulture);
        var baseName = $"{FilePrefix}-{date}";
        var path = Path.Combine(_options.DirectoryPath, baseName + FileExtension);

        for (var part = 1; File.Exists(path) && new FileInfo(path).Length >= _options.FileSizeLimitBytes; part++)
            path = Path.Combine(_options.DirectoryPath, $"{baseName}-{part:D3}{FileExtension}");

        return path;
    }

    private void EnsureDirectoryAndPurge()
    {
        if (!_directoryReady)
        {
            Directory.CreateDirectory(_options.DirectoryPath);
            _directoryReady = true;
        }

        var today = DateOnly.FromDateTime(_timeProvider.GetLocalNow().DateTime);
        if (_lastPurgeDate == today)
            return;

        _lastPurgeDate = today;
        PurgeOldLogs(today);
    }

    private void PurgeOldLogs(DateOnly today)
    {
        var cutoff = today.AddDays(-(_options.RetentionDays - 1));

        foreach (var path in Directory.EnumerateFiles(
            _options.DirectoryPath, $"{FilePrefix}-*{FileExtension}"))
        {
            var match = FileNamePattern.Match(Path.GetFileName(path));
            if (!match.Success)
                continue;

            if (!DateOnly.TryParseExact(
                    match.Groups["date"].Value,
                    "yyyyMMdd",
                    CultureInfo.InvariantCulture,
                    DateTimeStyles.None,
                    out var fileDate))
            {
                continue;
            }

            if (fileDate >= cutoff)
                continue;

            try
            {
                File.Delete(path);
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
            {
                // Retention is best-effort; the next start retries.
            }
        }
    }

    private static string NormalizeCategory(string? category)
    {
        var safe = LogSanitizer.Redact(category)
            .Replace('[', '(')
            .Replace(']', ')');

        if (string.IsNullOrWhiteSpace(safe))
            return LogCategories.App;

        return safe.Length <= MaxCategoryLength ? safe : safe[..MaxCategoryLength];
    }
}
