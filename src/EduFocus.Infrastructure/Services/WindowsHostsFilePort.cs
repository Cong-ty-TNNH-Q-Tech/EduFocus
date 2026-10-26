using System.Diagnostics;
using System.Text;
using EduFocus.Domain.Entities;
using EduFocus.Domain.Enums;
using EduFocus.Ports.Outbound.Services;

namespace EduFocus.Infrastructure.Services;

public interface IHostsFileStorage
{
    Task<byte[]?> ReadAsync(string path, CancellationToken cancellationToken);
    Task WriteAtomicallyAsync(string path, byte[] contents, CancellationToken cancellationToken);
    Task DeleteAsync(string path, CancellationToken cancellationToken);
}

public interface IDnsCacheFlusher
{
    Task FlushAsync(CancellationToken cancellationToken);
}

public sealed class PhysicalHostsFileStorage : IHostsFileStorage
{
    public async Task<byte[]?> ReadAsync(string path, CancellationToken cancellationToken) =>
        File.Exists(path) ? await File.ReadAllBytesAsync(path, cancellationToken) : null;

    public async Task WriteAtomicallyAsync(string path, byte[] contents, CancellationToken cancellationToken)
    {
        var directory = Path.GetDirectoryName(Path.GetFullPath(path))!;
        Directory.CreateDirectory(directory);
        var temporary = Path.Combine(directory, $".{Path.GetFileName(path)}.{Guid.NewGuid():N}.tmp");
        try
        {
            await using (var stream = new FileStream(temporary, FileMode.CreateNew, FileAccess.Write, FileShare.None,
                             4096, FileOptions.Asynchronous | FileOptions.WriteThrough))
            {
                await stream.WriteAsync(contents, cancellationToken);
                await stream.FlushAsync(cancellationToken);
                stream.Flush(flushToDisk: true);
            }
            if (File.Exists(path)) File.Replace(temporary, path, null, ignoreMetadataErrors: true);
            else File.Move(temporary, path);
        }
        finally
        {
            if (File.Exists(temporary)) File.Delete(temporary);
        }
    }

    public Task DeleteAsync(string path, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        if (File.Exists(path)) File.Delete(path);
        return Task.CompletedTask;
    }
}

public sealed class WindowsDnsCacheFlusher : IDnsCacheFlusher
{
    public async Task FlushAsync(CancellationToken cancellationToken)
    {
        if (!OperatingSystem.IsWindows()) throw new PlatformNotSupportedException("The Windows DNS cache can only be flushed on Windows.");
        using var process = new Process
        {
            StartInfo = new ProcessStartInfo("ipconfig.exe", "/flushdns")
            {
                UseShellExecute = false,
                CreateNoWindow = true,
                RedirectStandardOutput = true,
                RedirectStandardError = true
            }
        };
        if (!process.Start()) throw new InvalidOperationException("The DNS cache flush process could not be started.");
        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        timeout.CancelAfter(TimeSpan.FromSeconds(10));
        try { await process.WaitForExitAsync(timeout.Token); }
        catch
        {
            try { process.Kill(entireProcessTree: true); } catch { }
            throw;
        }
        if (process.ExitCode != 0) throw new InvalidOperationException("Windows did not confirm the DNS cache flush.");
    }
}

/// <summary>Atomically manages only the EduFocus marker section and keeps an exact pre-change backup.</summary>
public sealed class WindowsHostsFilePort : IHostsFilePort
{
    private const string BeginMarker = "# >>> EduFocus managed hosts BEGIN v1 >>>";
    private const string EndMarker = "# <<< EduFocus managed hosts END v1 <<<";
    private static readonly byte[] BackupMagic = Encoding.ASCII.GetBytes("EDUFOCUS-HOSTS-BACKUP-1\n");
    private static readonly SemaphoreSlim Gate = new(1, 1);
    private readonly string _hostsPath;
    private readonly string _backupPath;
    private readonly IHostsFileStorage _storage;
    private readonly IDnsCacheFlusher _dns;
    private readonly IAppLogger _logger;

    public WindowsHostsFilePort(string hostsPath, string backupPath, IHostsFileStorage storage, IDnsCacheFlusher dns, IAppLogger logger)
    {
        _hostsPath = Path.GetFullPath(hostsPath);
        _backupPath = Path.GetFullPath(backupPath);
        _storage = storage ?? throw new ArgumentNullException(nameof(storage));
        _dns = dns ?? throw new ArgumentNullException(nameof(dns));
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
    }

    public async Task ApplyAsync(IReadOnlyCollection<BlockRule> rules, DateTimeOffset now, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(rules);
        if (now.Offset != TimeSpan.Zero) throw new ArgumentException("Timestamp must be UTC.", nameof(now));
        await Gate.WaitAsync(cancellationToken);
        try
        {
            var current = await _storage.ReadAsync(_hostsPath, cancellationToken) ?? Array.Empty<byte>();
            var region = FindRegion(current);
            var domains = ResolveBlockedDomains(rules, now);
            if (domains.Count == 0 && region is null) return;

            var backup = await _storage.ReadAsync(_backupPath, cancellationToken);
            var separatorInserted = false;
            var createdBackup = false;
            if (region is null)
            {
                if (backup is not null)
                {
                    await _storage.DeleteAsync(_backupPath, cancellationToken);
                    backup = null;
                }
                separatorInserted = current.Length > 0 && !EndsInNewline(current);
                backup = PackBackup(await _storage.ReadAsync(_hostsPath, cancellationToken), separatorInserted);
                await _storage.WriteAtomicallyAsync(_backupPath, backup, cancellationToken);
                createdBackup = true;
            }
            else if (backup is not null)
            {
                separatorInserted = UnpackBackup(backup).SeparatorInserted;
            }

            var next = domains.Count == 0
                ? RemoveRegion(current, region!.Value, separatorInserted)
                : ReplaceRegion(current, region, BuildRegion(domains, current));
            if (!next.AsSpan().SequenceEqual(current))
            {
                try { await ReplaceAndFlushAsync(current, next, cancellationToken); }
                catch
                {
                    if (createdBackup)
                    {
                        var afterFailure = await _storage.ReadAsync(_hostsPath, CancellationToken.None);
                        if ((afterFailure ?? Array.Empty<byte>()).AsSpan().SequenceEqual(current))
                            await _storage.DeleteAsync(_backupPath, CancellationToken.None);
                    }
                    throw;
                }
                _logger.Info($"EduFocus hosts rules synchronized ({domains.Count} exact domain(s)).");
            }
            if (domains.Count == 0) await _storage.DeleteAsync(_backupPath, cancellationToken);
        }
        catch (Exception ex)
        {
            _logger.Error("EduFocus could not synchronize its managed hosts entries; Protection must remain unchanged.", ex);
            throw;
        }
        finally { Gate.Release(); }
    }

    public async Task RestoreAsync(CancellationToken cancellationToken = default)
    {
        await Gate.WaitAsync(cancellationToken);
        try
        {
            var current = await _storage.ReadAsync(_hostsPath, cancellationToken);
            var backup = await _storage.ReadAsync(_backupPath, cancellationToken);
            if (current is null)
            {
                if (backup is not null)
                {
                    var saved = UnpackBackup(backup);
                    if (saved.OriginallyExisted)
                    {
                        await _storage.WriteAtomicallyAsync(_hostsPath, saved.Original, cancellationToken);
                        await _dns.FlushAsync(cancellationToken);
                    }
                    await _storage.DeleteAsync(_backupPath, cancellationToken);
                }
                return;
            }
            Region? region;
            try { region = FindRegion(current); }
            catch (InvalidDataException) when (backup is not null)
            {
                var saved = UnpackBackup(backup);
                if (saved.OriginallyExisted)
                {
                    await _storage.WriteAtomicallyAsync(_hostsPath, saved.Original, cancellationToken);
                    await _dns.FlushAsync(cancellationToken);
                }
                else
                {
                    await _storage.DeleteAsync(_hostsPath, cancellationToken);
                    await _dns.FlushAsync(cancellationToken);
                }
                await _storage.DeleteAsync(_backupPath, cancellationToken);
                _logger.Warning("EduFocus restored the original hosts backup after its managed marker region was damaged.");
                return;
            }
            if (region is null)
            {
                // Atomic hosts replacement means no marker indicates the managed entries are absent.
                // Keep any host changes made outside our marker and discard the now-stale backup.
                if (backup is not null) await _storage.DeleteAsync(_backupPath, cancellationToken);
                return;
            }

            var metadata = backup is null ? (OriginallyExisted: true, SeparatorInserted: false, Original: Array.Empty<byte>()) : UnpackBackup(backup);
            var restored = RemoveRegion(current, region.Value, metadata.SeparatorInserted);
            try
            {
                if (metadata.OriginallyExisted) await ReplaceAndFlushAsync(current, restored, cancellationToken);
                else
                {
                    await _storage.DeleteAsync(_hostsPath, cancellationToken);
                    try { await _dns.FlushAsync(cancellationToken); }
                    catch
                    {
                        await _storage.WriteAtomicallyAsync(_hostsPath, current, CancellationToken.None);
                        try { await _dns.FlushAsync(CancellationToken.None); } catch { }
                        throw;
                    }
                }
                await _storage.DeleteAsync(_backupPath, cancellationToken);
                _logger.Info("EduFocus managed hosts entries were restored; other hosts lines were preserved.");
            }
            catch
            {
                // Atomic rollback leaves the previous managed content available for a safe retry.
                throw;
            }
        }
        catch (Exception ex)
        {
            _logger.Error("EduFocus could not restore its managed hosts entries; Protection must remain unchanged.", ex);
            throw;
        }
        finally { Gate.Release(); }
    }

    private async Task ReplaceAndFlushAsync(byte[] previous, byte[] next, CancellationToken cancellationToken)
    {
        await _storage.WriteAtomicallyAsync(_hostsPath, next, cancellationToken);
        try { await _dns.FlushAsync(cancellationToken); }
        catch
        {
            await _storage.WriteAtomicallyAsync(_hostsPath, previous, CancellationToken.None);
            try { await _dns.FlushAsync(CancellationToken.None); } catch { }
            throw;
        }
    }

    private static SortedSet<string> ResolveBlockedDomains(IReadOnlyCollection<BlockRule> rules, DateTimeOffset now)
    {
        var applicable = rules.Where(rule => rule.RuleType == BlockRuleType.Domain && rule.MatchType == BlockMatchType.Exact && rule.IsApplicable(now)).ToArray();
        var result = new SortedSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var rule in applicable)
        {
            if (rule.Effect != BlockRuleEffect.Block || !IsSafeHostEntry(rule.NormalizedValue)) continue;
            if (applicable.Any(allow => allow.Effect == BlockRuleEffect.Allow &&
                                        string.Equals(allow.NormalizedValue, rule.NormalizedValue, StringComparison.OrdinalIgnoreCase))) continue;
            result.Add(rule.NormalizedValue);
        }
        return result;
    }

    private static bool IsSafeHostEntry(string domain) => domain.Length <= 253 &&
        Uri.CheckHostName(domain) == UriHostNameType.Dns &&
        !domain.Equals("localhost", StringComparison.OrdinalIgnoreCase) &&
        !domain.EndsWith(".localhost", StringComparison.OrdinalIgnoreCase) &&
        !domain.EndsWith(".local", StringComparison.OrdinalIgnoreCase);

    private static byte[] BuildRegion(IEnumerable<string> domains, byte[] current)
    {
        var newline = EndsInCrlf(current) || (!Encoding.ASCII.GetString(current).Contains("\n", StringComparison.Ordinal)) ? "\r\n" : "\n";
        var builder = new StringBuilder().Append(BeginMarker).Append(newline);
        foreach (var domain in domains)
        {
            builder.Append("127.0.0.1\t").Append(domain).Append(newline);
            builder.Append("::1\t").Append(domain).Append(newline);
        }
        builder.Append(EndMarker).Append(newline);
        return Encoding.ASCII.GetBytes(builder.ToString());
    }

    private static byte[] ReplaceRegion(byte[] current, Region? region, byte[] block)
    {
        var before = region is null ? current : current[..region.Value.Start];
        var after = region is null ? Array.Empty<byte>() : current[region.Value.End..];
        if (block.Length == 0) return Join(before, after);
        var needsSeparator = before.Length > 0 && !EndsInNewline(before);
        return Join(before, needsSeparator ? new byte[] { (byte)'\r', (byte)'\n' } : Array.Empty<byte>(), block, after);
    }

    private static byte[] RemoveRegion(byte[] current, Region region, bool separatorInserted)
    {
        var before = current[..region.Start];
        var after = current[region.End..];
        if (separatorInserted && after.Length == 0)
        {
            if (before.AsSpan().EndsWith(new byte[] { 13, 10 })) before = before[..^2];
            else if (before.AsSpan().EndsWith(new byte[] { 10 })) before = before[..^1];
        }
        return Join(before, after);
    }

    private static Region? FindRegion(byte[] bytes)
    {
        var lines = new List<(int Start, int ContentEnd, int End, string Text)>();
        for (var lineStart = 0; lineStart < bytes.Length;)
        {
            var newline = Array.IndexOf(bytes, (byte)'\n', lineStart);
            var lineEnd = newline < 0 ? bytes.Length : newline + 1;
            var contentEnd = newline < 0 ? lineEnd : newline;
            if (contentEnd > lineStart && bytes[contentEnd - 1] == (byte)'\r') contentEnd--;
            var text = Encoding.ASCII.GetString(bytes, lineStart, contentEnd - lineStart).Trim();
            lines.Add((lineStart, contentEnd, lineEnd, text));
            lineStart = lineEnd;
        }
        var begins = lines.Select((line, index) => (line, index)).Where(x => x.line.Text == BeginMarker).ToArray();
        var ends = lines.Select((line, index) => (line, index)).Where(x => x.line.Text == EndMarker).ToArray();
        if (begins.Length == 0 && ends.Length == 0) return null;
        if (begins.Length != 1 || ends.Length != 1 || begins[0].index >= ends[0].index)
            throw new InvalidDataException("The EduFocus hosts markers are missing, duplicated, or out of order; no hosts changes were made.");
        var regionStart = lines[begins[0].index].Start;
        var regionEnd = lines[ends[0].index].End;
        return new Region(regionStart, regionEnd);
    }

    private static byte[] PackBackup(byte[]? original, bool separatorInserted)
    {
        using var stream = new MemoryStream();
        stream.Write(BackupMagic);
        stream.WriteByte(original is null ? (byte)0 : (byte)1);
        stream.WriteByte(separatorInserted ? (byte)1 : (byte)0);
        if (original is not null) stream.Write(original);
        return stream.ToArray();
    }

    private static (bool OriginallyExisted, bool SeparatorInserted, byte[] Original) UnpackBackup(byte[] backup)
    {
        if (backup.Length < BackupMagic.Length + 2 || !backup.AsSpan(0, BackupMagic.Length).SequenceEqual(BackupMagic))
            throw new InvalidDataException("The EduFocus hosts backup is invalid; no hosts changes were made.");
        var existed = backup[BackupMagic.Length] == 1;
        var separator = backup[BackupMagic.Length + 1] == 1;
        return (existed, separator, backup[(BackupMagic.Length + 2)..]);
    }

    private static byte[] Join(params byte[][] parts)
    {
        var length = parts.Sum(part => part.Length);
        var result = new byte[length];
        var offset = 0;
        foreach (var part in parts) { Buffer.BlockCopy(part, 0, result, offset, part.Length); offset += part.Length; }
        return result;
    }

    private static bool EndsInNewline(byte[] bytes) => bytes.Length > 0 && bytes[^1] == (byte)'\n';
    private static bool EndsInCrlf(byte[] bytes) => bytes.Length > 1 && bytes[^2] == (byte)'\r' && bytes[^1] == (byte)'\n';
    private readonly record struct Region(int Start, int End);
}
