using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;

namespace HostsBlockingSpike.Hosts;

public sealed record HostsApplyResult(
    string HostsPath,
    string BackupPath,
    IReadOnlyList<string> Domains,
    bool Changed,
    int BlockLineCount,
    string OriginalSha256,
    string UpdatedSha256);

public sealed record HostsRestoreResult(
    string HostsPath,
    bool BlockRemoved,
    bool RestoredExact,
    string FinalSha256);

public sealed record HostsRecoveryResult(bool Performed, bool FromJournal, string Detail);

public sealed partial class HostsFileManager
{
    public const string StartMarker = "### EDUFOCUS BLOCK START ###";
    public const string EndMarker = "### EDUFOCUS BLOCK END ###";

    private static readonly Encoding FileEncoding = Encoding.Latin1;

    [GeneratedRegex(@"^[a-z0-9](?:[a-z0-9-]{0,61}[a-z0-9])?(?:\.[a-z0-9](?:[a-z0-9-]{0,61}[a-z0-9])?)+$", RegexOptions.CultureInvariant)]
    private static partial Regex DomainRegex();

    private readonly string _backupDir;
    private readonly string _journalPath;

    public HostsFileManager(string hostsPath, string backupDir)
    {
        HostsPath = hostsPath;
        _backupDir = backupDir;
        _journalPath = Path.Combine(backupDir, "journal.json");
    }

    public string HostsPath { get; }

    public string ReadText() => FileEncoding.GetString(File.ReadAllBytes(HostsPath));

    public bool ContainsBlockSection(string text) =>
        text.Contains(StartMarker, StringComparison.Ordinal) ||
        text.Contains(EndMarker, StringComparison.Ordinal);

    public HostsApplyResult Apply(IEnumerable<string> domains)
    {
        var normalized = NormalizeDomains(domains);
        Directory.CreateDirectory(_backupDir);

        var originalBytes = File.ReadAllBytes(HostsPath);
        var originalText = FileEncoding.GetString(originalBytes);
        var backupPath = EnsureBackup(originalBytes);

        var stripped = RemoveBlockSection(originalText);
        var newline = DetectNewline(originalText);
        var builder = new StringBuilder(stripped);
        if (builder.Length > 0 && builder[^1] != '\n')
            builder.Append(newline);

        builder.Append(StartMarker).Append(newline);
        foreach (var domain in normalized)
            builder.Append("127.0.0.1 ").Append(domain).Append(newline);
        builder.Append(EndMarker).Append(newline);

        var updatedBytes = FileEncoding.GetBytes(builder.ToString());
        var changed = !originalBytes.AsSpan().SequenceEqual(updatedBytes);
        if (changed)
            WriteWithRetry(updatedBytes);

        return new HostsApplyResult(
            HostsPath,
            backupPath,
            normalized,
            changed,
            normalized.Count + 2,
            Sha256(originalBytes),
            Sha256(updatedBytes));
    }

    public HostsRestoreResult Restore()
    {
        if (!File.Exists(HostsPath))
            return new HostsRestoreResult(HostsPath, false, false, string.Empty);

        var currentBytes = File.ReadAllBytes(HostsPath);
        var currentText = FileEncoding.GetString(currentBytes);
        if (!ContainsBlockSection(currentText))
        {
            DeleteJournal();
            return new HostsRestoreResult(HostsPath, false, true, Sha256(currentBytes));
        }

        var stripped = RemoveBlockSection(currentText);
        var journal = LoadJournal();
        var originalBytes = journal is not null && File.Exists(journal.BackupPath)
            ? File.ReadAllBytes(journal.BackupPath)
            : null;

        byte[] targetBytes;
        var exact = false;
        if (originalBytes is not null)
        {
            var originalText = FileEncoding.GetString(originalBytes);
            if (TrimTrailingNewlines(RemoveBlockSection(originalText)) == TrimTrailingNewlines(stripped))
            {
                targetBytes = originalBytes;
                exact = true;
            }
            else
            {
                targetBytes = FileEncoding.GetBytes(stripped);
            }
        }
        else
        {
            targetBytes = FileEncoding.GetBytes(stripped);
        }

        if (!currentBytes.AsSpan().SequenceEqual(targetBytes))
            WriteWithRetry(targetBytes);

        DeleteJournal();
        return new HostsRestoreResult(HostsPath, true, exact, Sha256(targetBytes));
    }

    public HostsRecoveryResult RecoverIfNeeded()
    {
        if (File.Exists(_journalPath))
        {
            var restore = Restore();
            return new HostsRecoveryResult(
                true,
                true,
                $"Journal found; blockRemoved={restore.BlockRemoved}, exact={restore.RestoredExact}");
        }

        var text = ReadText();
        if (!ContainsBlockSection(text))
            return new HostsRecoveryResult(false, false, "No block section or journal found.");

        WriteWithRetry(FileEncoding.GetBytes(RemoveBlockSection(text)));
        return new HostsRecoveryResult(true, false, "Removed orphan EduFocus block section.");
    }

    public static IReadOnlyList<string> NormalizeDomains(IEnumerable<string> domains) =>
        domains
            .Select(NormalizeDomain)
            .Distinct(StringComparer.Ordinal)
            .Order(StringComparer.Ordinal)
            .ToList();

    public static string NormalizeDomain(string raw)
    {
        var domain = raw.Trim().TrimEnd('.').ToLowerInvariant();
        if (domain.Length is 0 or > 253 || !DomainRegex().IsMatch(domain))
            throw new ArgumentException($"Invalid block domain: '{raw}'");

        return domain;
    }

    private string EnsureBackup(byte[] originalBytes)
    {
        var journal = LoadJournal();
        if (journal is not null && File.Exists(journal.BackupPath))
            return journal.BackupPath;

        var backupPath = Path.Combine(_backupDir, $"hosts-{DateTime.UtcNow:yyyyMMdd-HHmmss-fff}.bak");
        var index = 0;
        while (File.Exists(backupPath))
        {
            backupPath = Path.Combine(
                _backupDir,
                $"hosts-{DateTime.UtcNow:yyyyMMdd-HHmmss-fff}-{++index}.bak");
        }

        File.WriteAllBytes(backupPath, originalBytes);
        SaveJournal(new HostsJournal(HostsPath, backupPath, Sha256(originalBytes), DateTimeOffset.UtcNow));
        return backupPath;
    }

    private void WriteWithRetry(byte[] bytes)
    {
        var directory = Path.GetDirectoryName(HostsPath)
            ?? throw new InvalidOperationException($"Hosts path has no directory: {HostsPath}");
        var tempPath = Path.Combine(directory, $"hosts.edufocus-{Guid.NewGuid():N}.tmp");

        for (var attempt = 0; ; attempt++)
        {
            try
            {
                File.WriteAllBytes(tempPath, bytes);
                File.Move(tempPath, HostsPath, overwrite: true);
                return;
            }
            catch (IOException) when (attempt < 2)
            {
                Thread.Sleep(200);
            }
            finally
            {
                if (File.Exists(tempPath))
                {
                    try { File.Delete(tempPath); }
                    catch (IOException) { }
                }
            }
        }
    }

    internal static string RemoveBlockSection(string text)
    {
        if (!text.Contains(StartMarker, StringComparison.Ordinal) &&
            !text.Contains(EndMarker, StringComparison.Ordinal))
        {
            return text;
        }

        var newline = DetectNewline(text);
        var endsWithNewline = text.EndsWith('\n');
        var rawLines = text.Split('\n');
        var lines = new List<string>(rawLines.Length);
        foreach (var raw in rawLines)
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

    private static string DetectNewline(string text) =>
        text.Contains("\r\n", StringComparison.Ordinal) ? "\r\n" : "\n";

    private static string TrimTrailingNewlines(string value) => value.TrimEnd('\r', '\n');

    private static string Sha256(byte[] bytes) => Convert.ToHexString(SHA256.HashData(bytes));

    private HostsJournal? LoadJournal()
    {
        if (!File.Exists(_journalPath))
            return null;

        try
        {
            return JsonSerializer.Deserialize<HostsJournal>(File.ReadAllText(_journalPath));
        }
        catch (JsonException)
        {
            return null;
        }
    }

    private void SaveJournal(HostsJournal journal) =>
        File.WriteAllText(_journalPath, JsonSerializer.Serialize(journal, new JsonSerializerOptions { WriteIndented = true }));

    private void DeleteJournal()
    {
        if (File.Exists(_journalPath))
            File.Delete(_journalPath);
    }

    private sealed record HostsJournal(
        string HostsPath,
        string BackupPath,
        string OriginalSha256,
        DateTimeOffset CreatedUtc);
}
