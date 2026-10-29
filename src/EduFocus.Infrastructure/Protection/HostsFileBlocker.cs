using System.Globalization;
using System.Text;
using System.Text.Json;
using EduFocus.Domain.ValueObjects;
using EduFocus.Ports.Outbound.Services;
using EduFocus.Ports.Protection;

namespace EduFocus.Infrastructure.Protection;

/// <summary>
/// Maintains the EduFocus section of the Windows hosts file (UC09,
/// system architecture §6.2). Only lines between the EduFocus markers are
/// changed; every other line is preserved. Apply is idempotent, restore keeps
/// edits made outside the markers, and a journal enables exact recovery after a
/// crash. The adapter never logs hosts content or domain values.
/// </summary>
public sealed class HostsFileBlocker : IHostsFileBlocker
{
    public const string StartMarker = "### EDUFOCUS BLOCK START ###";
    public const string EndMarker = "### EDUFOCUS BLOCK END ###";

    private const string JournalFileName = "journal.json";
    private static readonly Encoding FileEncoding = Encoding.Latin1;

    private readonly IDnsFlusher _dnsFlusher;
    private readonly IAppLogger _logger;
    private readonly string _hostsPath;
    private readonly string _backupDirectory;
    private readonly string _journalPath;
    private readonly int _writeAttempts;
    private readonly TimeSpan _writeRetryDelay;
    private readonly SemaphoreSlim _gate = new(1, 1);

    public HostsFileBlocker(HostsFileOptions options, IDnsFlusher dnsFlusher, IAppLogger logger)
    {
        ArgumentNullException.ThrowIfNull(options);
        _dnsFlusher = dnsFlusher ?? throw new ArgumentNullException(nameof(dnsFlusher));
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));

        if (string.IsNullOrWhiteSpace(options.HostsPath))
            throw new ArgumentException("A hosts file path is required.", nameof(options));
        if (string.IsNullOrWhiteSpace(options.BackupDirectory))
            throw new ArgumentException("A backup directory is required.", nameof(options));
        if (options.WriteAttempts is < 1 or > 10)
            throw new ArgumentOutOfRangeException(nameof(options), "Write attempts must be between 1 and 10.");
        if (options.WriteRetryDelay < TimeSpan.Zero || options.WriteRetryDelay > TimeSpan.FromSeconds(5))
            throw new ArgumentOutOfRangeException(nameof(options), "The write retry delay must be between 0 and 5 seconds.");

        _hostsPath = Path.GetFullPath(options.HostsPath);
        _backupDirectory = Path.GetFullPath(options.BackupDirectory);
        _journalPath = Path.Combine(_backupDirectory, JournalFileName);
        _writeAttempts = options.WriteAttempts;
        _writeRetryDelay = options.WriteRetryDelay;
    }

    public async Task<HostsApplyResult> ApplyAsync(
        IReadOnlyCollection<string> domains,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(domains);
        var normalized = NormalizeDomains(domains);

        await _gate.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            var originalBytes = await ReadHostsAsync("apply", cancellationToken).ConfigureAwait(false);
            var originalText = FileEncoding.GetString(originalBytes);
            var backupPath = await EnsureBackupAsync(originalBytes, "apply", cancellationToken)
                .ConfigureAwait(false);

            var builder = new StringBuilder(RemoveBlockSection(originalText));
            var newline = DetectNewline(originalText);
            if (builder.Length > 0 && builder[^1] != '\n')
                builder.Append(newline);

            builder.Append(StartMarker).Append(newline);
            foreach (var domain in normalized)
            {
                builder.Append("127.0.0.1 ").Append(domain).Append(newline);
                builder.Append("::1 ").Append(domain).Append(newline);
            }

            builder.Append(EndMarker).Append(newline);

            var updatedBytes = FileEncoding.GetBytes(builder.ToString());
            var changed = !originalBytes.AsSpan().SequenceEqual(updatedBytes);
            if (changed)
            {
                await WriteHostsAsync(updatedBytes, "apply", cancellationToken).ConfigureAwait(false);
                await FlushDnsAsync(cancellationToken).ConfigureAwait(false);
                _logger.Info(
                    LogCategories.Protection,
                    FormattableString.Invariant(
                        $"Hosts block section updated with {normalized.Count} domains."));
            }

            return new HostsApplyResult(changed, normalized.Count, normalized, backupPath);
        }
        finally
        {
            _gate.Release();
        }
    }

    public async Task<HostsRestoreResult> RestoreAsync(CancellationToken cancellationToken = default)
    {
        await _gate.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            return await RestoreCoreAsync("restore", cancellationToken).ConfigureAwait(false);
        }
        finally
        {
            _gate.Release();
        }
    }

    public async Task<HostsRecoveryResult> SelfHealAsync(CancellationToken cancellationToken = default)
    {
        await _gate.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            if (File.Exists(_journalPath))
            {
                await RestoreCoreAsync("self-heal", cancellationToken).ConfigureAwait(false);
                _logger.Warning(
                    LogCategories.Protection,
                    "Recovered the hosts file after an interrupted EduFocus change.");
                return new HostsRecoveryResult(Performed: true, FromJournal: true);
            }

            var currentBytes = await ReadHostsIfPresentAsync("self-heal", cancellationToken).ConfigureAwait(false);
            if (currentBytes is null)
                return new HostsRecoveryResult(Performed: false, FromJournal: false);

            var currentText = FileEncoding.GetString(currentBytes);
            if (!ContainsBlockSection(currentText))
                return new HostsRecoveryResult(Performed: false, FromJournal: false);

            await WriteHostsAsync(
                    FileEncoding.GetBytes(RemoveBlockSection(currentText)),
                    "self-heal",
                    cancellationToken)
                .ConfigureAwait(false);
            await FlushDnsAsync(cancellationToken).ConfigureAwait(false);
            _logger.Warning(
                LogCategories.Protection,
                "Removed an orphan EduFocus block section from the hosts file.");

            return new HostsRecoveryResult(Performed: true, FromJournal: false);
        }
        finally
        {
            _gate.Release();
        }
    }

    private async Task<HostsRestoreResult> RestoreCoreAsync(
        string operation,
        CancellationToken cancellationToken)
    {
        var currentBytes = await ReadHostsIfPresentAsync(operation, cancellationToken).ConfigureAwait(false);
        if (currentBytes is null)
        {
            DeleteJournalBestEffort();
            return new HostsRestoreResult(BlockRemoved: false, RestoredExact: false);
        }

        var currentText = FileEncoding.GetString(currentBytes);
        if (!ContainsBlockSection(currentText))
        {
            DeleteJournalBestEffort();
            return new HostsRestoreResult(BlockRemoved: false, RestoredExact: true);
        }

        var stripped = RemoveBlockSection(currentText);
        var journal = await LoadJournalAsync().ConfigureAwait(false);
        var exact = false;
        var targetBytes = FileEncoding.GetBytes(stripped);

        if (journal is not null
            && SamePath(journal.HostsPath, _hostsPath)
            && File.Exists(journal.BackupPath))
        {
            var originalBytes = await TryReadBackupAsync(journal.BackupPath, cancellationToken)
                .ConfigureAwait(false);

            if (originalBytes is not null)
            {
                var originalText = FileEncoding.GetString(originalBytes);
                if (string.Equals(
                        TrimTrailingNewlines(RemoveBlockSection(originalText)),
                        TrimTrailingNewlines(stripped),
                        StringComparison.Ordinal))
                {
                    targetBytes = originalBytes;
                    exact = true;
                }
            }
        }

        if (!currentBytes.AsSpan().SequenceEqual(targetBytes))
        {
            await WriteHostsAsync(targetBytes, operation, cancellationToken).ConfigureAwait(false);
            await FlushDnsAsync(cancellationToken).ConfigureAwait(false);
            _logger.Info(
                LogCategories.Protection,
                "EduFocus block section removed from the hosts file.");
        }

        DeleteJournalBestEffort();
        return new HostsRestoreResult(BlockRemoved: true, RestoredExact: exact);
    }

    private async Task<byte[]?> TryReadBackupAsync(string backupPath, CancellationToken cancellationToken)
    {
        try
        {
            return await File.ReadAllBytesAsync(backupPath, cancellationToken).ConfigureAwait(false);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            _logger.Warning(
                LogCategories.Protection,
                "The hosts backup could not be read; the block section will be removed without an exact restore.");
            return null;
        }
    }

    private async Task<string> EnsureBackupAsync(
        byte[] hostsBytes,
        string operation,
        CancellationToken cancellationToken)
    {
        var journal = await LoadJournalAsync().ConfigureAwait(false);
        if (journal is not null
            && SamePath(journal.HostsPath, _hostsPath)
            && File.Exists(journal.BackupPath))
        {
            return journal.BackupPath;
        }

        var cleanBytes = FileEncoding.GetBytes(RemoveBlockSection(FileEncoding.GetString(hostsBytes)));

        try
        {
            Directory.CreateDirectory(_backupDirectory);
            var backupPath = NextBackupPath();
            await File.WriteAllBytesAsync(backupPath, cleanBytes, cancellationToken).ConfigureAwait(false);
            await SaveJournalAsync(new HostsJournal(_hostsPath, backupPath, DateTimeOffset.UtcNow))
                .ConfigureAwait(false);
            return backupPath;
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch (UnauthorizedAccessException ex)
        {
            throw new HostsFileException(
                operation,
                HostsFileFailureReason.AccessDenied,
                "Could not create a backup of the hosts file.",
                ex);
        }
        catch (IOException ex)
        {
            throw new HostsFileException(
                operation,
                HostsFileFailureReason.WriteFailed,
                "Could not create a backup of the hosts file.",
                ex);
        }
    }

    private string NextBackupPath()
    {
        var stamp = DateTime.UtcNow.ToString("yyyyMMdd-HHmmss-fff", CultureInfo.InvariantCulture);
        var path = Path.Combine(_backupDirectory, $"hosts-{stamp}.bak");
        var index = 0;
        while (File.Exists(path))
            path = Path.Combine(_backupDirectory, $"hosts-{stamp}-{++index}.bak");

        return path;
    }

    private async Task<byte[]> ReadHostsAsync(string operation, CancellationToken cancellationToken)
    {
        try
        {
            return await File.ReadAllBytesAsync(_hostsPath, cancellationToken).ConfigureAwait(false);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch (FileNotFoundException ex)
        {
            throw new HostsFileException(
                operation,
                HostsFileFailureReason.NotFound,
                "The hosts file does not exist.",
                ex);
        }
        catch (DirectoryNotFoundException ex)
        {
            throw new HostsFileException(
                operation,
                HostsFileFailureReason.NotFound,
                "The hosts file directory does not exist.",
                ex);
        }
        catch (UnauthorizedAccessException ex)
        {
            throw new HostsFileException(
                operation,
                HostsFileFailureReason.AccessDenied,
                "Access to the hosts file was denied.",
                ex);
        }
        catch (IOException ex)
        {
            throw new HostsFileException(
                operation,
                HostsFileFailureReason.Locked,
                "The hosts file is locked by another process.",
                ex);
        }
    }

    private async Task<byte[]?> ReadHostsIfPresentAsync(
        string operation,
        CancellationToken cancellationToken)
    {
        try
        {
            return await ReadHostsAsync(operation, cancellationToken).ConfigureAwait(false);
        }
        catch (HostsFileException ex) when (ex.Reason == HostsFileFailureReason.NotFound)
        {
            return null;
        }
    }

    private async Task WriteHostsAsync(
        byte[] bytes,
        string operation,
        CancellationToken cancellationToken)
    {
        var directory = Path.GetDirectoryName(_hostsPath);
        if (string.IsNullOrEmpty(directory))
        {
            throw new HostsFileException(
                operation,
                HostsFileFailureReason.WriteFailed,
                "The hosts file has no parent directory.");
        }

        var tempPath = Path.Combine(directory, $"hosts.edufocus-{Guid.NewGuid():N}.tmp");
        try
        {
            for (var attempt = 1; ; attempt++)
            {
                try
                {
                    await File.WriteAllBytesAsync(tempPath, bytes, cancellationToken).ConfigureAwait(false);
                    File.Move(tempPath, _hostsPath, overwrite: true);
                    return;
                }
                catch (IOException) when (attempt < _writeAttempts)
                {
                    await Task.Delay(_writeRetryDelay, cancellationToken).ConfigureAwait(false);
                }
                catch (UnauthorizedAccessException ex)
                {
                    var locked = IsLockedByAnotherProcess();
                    if (locked && attempt < _writeAttempts)
                    {
                        await Task.Delay(_writeRetryDelay, cancellationToken).ConfigureAwait(false);
                        continue;
                    }

                    throw new HostsFileException(
                        operation,
                        locked ? HostsFileFailureReason.Locked : HostsFileFailureReason.AccessDenied,
                        locked
                            ? "The hosts file is locked by another process."
                            : "Access to the hosts file was denied.",
                        ex);
                }
                catch (IOException ex) when (IsSharingViolation(ex))
                {
                    throw new HostsFileException(
                        operation,
                        HostsFileFailureReason.Locked,
                        "The hosts file is locked by another process.",
                        ex);
                }
                catch (IOException ex)
                {
                    throw new HostsFileException(
                        operation,
                        HostsFileFailureReason.WriteFailed,
                        "The hosts file could not be written.",
                        ex);
                }
            }
        }
        finally
        {
            TryDelete(tempPath);
        }
    }

    private async Task FlushDnsAsync(CancellationToken cancellationToken)
    {
        try
        {
            await _dnsFlusher.FlushAsync(cancellationToken).ConfigureAwait(false);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception ex)
        {
            _logger.Warning(
                LogCategories.Protection,
                $"Could not flush the DNS cache after a hosts change ({ex.GetType().Name}).");
        }
    }

    private static IReadOnlyList<string> NormalizeDomains(IEnumerable<string> domains)
    {
        var normalized = new SortedSet<string>(StringComparer.Ordinal);

        foreach (var raw in domains)
        {
            if (string.IsNullOrWhiteSpace(raw))
                throw new ArgumentException("A block domain is required.", nameof(domains));

            var candidate = raw.Trim();
            if (candidate.Any(char.IsWhiteSpace)
                || candidate.IndexOfAny(['/', '\\', '@', '*', '?', '#', ':']) >= 0)
            {
                throw new ArgumentException($"Invalid block domain: '{candidate}'.", nameof(domains));
            }

            if (!DomainName.TryParse(candidate, out var domain))
                throw new ArgumentException($"Invalid block domain: '{candidate}'.", nameof(domains));

            normalized.Add(domain!.Value);
        }

        return normalized.ToList();
    }

    private async Task<HostsJournal?> LoadJournalAsync()
    {
        if (!File.Exists(_journalPath))
            return null;

        try
        {
            var json = await File.ReadAllTextAsync(_journalPath).ConfigureAwait(false);
            return JsonSerializer.Deserialize<HostsJournal>(json);
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (Exception ex) when (ex is JsonException or IOException or UnauthorizedAccessException)
        {
            _logger.Warning(
                LogCategories.Protection,
                "The hosts backup journal is unreadable; the block section will be removed without an exact restore.");
            return null;
        }
    }

    private Task SaveJournalAsync(HostsJournal journal) =>
        File.WriteAllTextAsync(_journalPath, JsonSerializer.Serialize(journal));

    private void DeleteJournalBestEffort()
    {
        try
        {
            if (File.Exists(_journalPath))
                File.Delete(_journalPath);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            _logger.Warning(LogCategories.Protection, "Could not delete the hosts backup journal.");
        }
    }

    private static void TryDelete(string path)
    {
        try
        {
            if (File.Exists(path))
                File.Delete(path);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
        }
    }

    private static bool ContainsBlockSection(string text) =>
        text.Contains(StartMarker, StringComparison.Ordinal)
        || text.Contains(EndMarker, StringComparison.Ordinal);

    private static string RemoveBlockSection(string text)
    {
        if (!ContainsBlockSection(text))
            return text;

        var newline = DetectNewline(text);
        var endsWithNewline = text.EndsWith('\n');
        var lines = new List<string>();
        foreach (var raw in text.Split('\n'))
            lines.Add(raw.EndsWith('\r') ? raw[..^1] : raw);

        if (endsWithNewline && lines.Count > 0)
            lines.RemoveAt(lines.Count - 1);

        var kept = new List<string>(lines.Count);
        var inBlock = false;
        foreach (var line in lines)
        {
            var trimmed = line.Trim();
            if (!inBlock && string.Equals(trimmed, StartMarker, StringComparison.Ordinal))
            {
                inBlock = true;
                continue;
            }

            if (inBlock)
            {
                if (string.Equals(trimmed, EndMarker, StringComparison.Ordinal))
                    inBlock = false;
                continue;
            }

            if (string.Equals(trimmed, EndMarker, StringComparison.Ordinal))
                continue;

            kept.Add(line);
        }

        if (kept.Count == 0)
            return string.Empty;

        var result = string.Join(newline, kept);
        return endsWithNewline ? result + newline : result;
    }

    private bool IsLockedByAnotherProcess()
    {
        try
        {
            using var probe = new FileStream(
                _hostsPath,
                FileMode.Open,
                FileAccess.ReadWrite,
                FileShare.None);
            return false;
        }
        catch (IOException)
        {
            return true;
        }
        catch (UnauthorizedAccessException)
        {
            return false;
        }
    }

    private static bool IsSharingViolation(IOException exception)
    {
        var code = exception.HResult & 0xFFFF;
        return code is 32 or 33;
    }

    private static string DetectNewline(string text) =>
        text.Contains("\r\n", StringComparison.Ordinal) ? "\r\n" : "\n";

    private static string TrimTrailingNewlines(string value) => value.TrimEnd('\r', '\n');

    private static bool SamePath(string left, string right) =>
        string.Equals(left, right, StringComparison.OrdinalIgnoreCase);

    private sealed record HostsJournal(string HostsPath, string BackupPath, DateTimeOffset CreatedUtc);
}
