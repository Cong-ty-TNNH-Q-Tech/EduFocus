using System.Diagnostics;
using System.Text;

namespace HostsBlockingSpike.Hosts;

public sealed record SelfTestResult(string Name, bool Passed, string Detail, long DurationMs);

public static class HostsSelfTests
{
    private const string SampleHosts = "# sample hosts\r\n# comment\r\n127.0.0.1 localhost\r\n";

    public static IReadOnlyList<SelfTestResult> RunAll(string workRoot)
    {
        return
        [
            Run("fresh apply + idempotency", workRoot, FreshApplyIsIdempotent),
            Run("restore exact bytes", workRoot, RestoreRestoresExactBytes),
            Run("restore without trailing newline", workRoot, RestoreWithoutTrailingNewline),
            Run("external edit preserved", workRoot, ExternalEditPreserved),
            Run("orphan marker self-heal", workRoot, OrphanMarkerSelfHeal),
            Run("crash recovery via journal", workRoot, CrashRecoveryViaJournal),
            Run("invalid domains rejected", workRoot, InvalidDomainsRejected)
        ];
    }

    private static SelfTestResult Run(string name, string workRoot, Func<TempScope, (bool Passed, string Detail)> body)
    {
        var stopwatch = Stopwatch.StartNew();
        using var scope = new TempScope(workRoot);
        try
        {
            var (passed, detail) = body(scope);
            stopwatch.Stop();
            return new SelfTestResult(name, passed, detail, stopwatch.ElapsedMilliseconds);
        }
        catch (Exception ex)
        {
            stopwatch.Stop();
            return new SelfTestResult(name, false, $"{ex.GetType().Name}: {ex.Message}", stopwatch.ElapsedMilliseconds);
        }
    }

    private static (bool, string) FreshApplyIsIdempotent(TempScope scope)
    {
        scope.WriteHosts(SampleHosts);
        var manager = scope.CreateManager();
        var first = manager.Apply(["example.com", "www.example.com", "example.com", "ExAmPle.COM."]);
        var firstBytes = scope.ReadHosts();
        var second = manager.Apply(["www.example.com", "example.com"]);
        var secondBytes = scope.ReadHosts();
        var text = Encoding.Latin1.GetString(secondBytes);

        var passed = first.Domains.Count == 2
            && first.Changed
            && !second.Changed
            && firstBytes.AsSpan().SequenceEqual(secondBytes)
            && text.Contains("127.0.0.1 example.com", StringComparison.Ordinal)
            && text.Contains("127.0.0.1 www.example.com", StringComparison.Ordinal)
            && text.Contains("# sample hosts", StringComparison.Ordinal);

        return (passed,
            $"domains={string.Join(',', first.Domains)}; firstChanged={first.Changed}; secondChanged={second.Changed}; identical={firstBytes.AsSpan().SequenceEqual(secondBytes)}");
    }

    private static (bool, string) RestoreRestoresExactBytes(TempScope scope)
    {
        var original = Encoding.Latin1.GetBytes(SampleHosts);
        File.WriteAllBytes(scope.HostsPath, original);
        var manager = scope.CreateManager();
        manager.Apply(["example.com"]);
        var restore = manager.Restore();
        var final = scope.ReadHosts();

        var passed = restore.BlockRemoved
            && restore.RestoredExact
            && final.AsSpan().SequenceEqual(original)
            && !manager.ContainsBlockSection(manager.ReadText());

        return (passed, $"blockRemoved={restore.BlockRemoved}; exact={restore.RestoredExact}; bytesEqual={final.AsSpan().SequenceEqual(original)}");
    }

    private static (bool, string) RestoreWithoutTrailingNewline(TempScope scope)
    {
        const string original = "127.0.0.1 localhost";
        scope.WriteHosts(original);
        var originalBytes = scope.ReadHosts();
        var manager = scope.CreateManager();
        manager.Apply(["example.com"]);
        var restore = manager.Restore();
        var final = scope.ReadHosts();

        var passed = restore.RestoredExact && final.AsSpan().SequenceEqual(originalBytes);
        return (passed, $"exact={restore.RestoredExact}; bytesEqual={final.AsSpan().SequenceEqual(originalBytes)}");
    }

    private static (bool, string) ExternalEditPreserved(TempScope scope)
    {
        scope.WriteHosts(SampleHosts);
        var manager = scope.CreateManager();
        manager.Apply(["example.com"]);

        var withEdit = new List<byte>(scope.ReadHosts());
        withEdit.AddRange(Encoding.Latin1.GetBytes("\r\n10.0.0.5 manual.local\r\n"));
        File.WriteAllBytes(scope.HostsPath, withEdit.ToArray());

        var restore = manager.Restore();
        var text = manager.ReadText();
        var passed = restore.BlockRemoved
            && !restore.RestoredExact
            && !manager.ContainsBlockSection(text)
            && text.Contains("10.0.0.5 manual.local", StringComparison.Ordinal)
            && text.Contains("127.0.0.1 localhost", StringComparison.Ordinal);

        return (passed, $"blockRemoved={restore.BlockRemoved}; exact={restore.RestoredExact}; editKept={text.Contains("manual.local", StringComparison.Ordinal)}");
    }

    private static (bool, string) OrphanMarkerSelfHeal(TempScope scope)
    {
        var orphan = SampleHosts + HostsFileManager.StartMarker + "\r\n127.0.0.1 example.com\r\n";
        scope.WriteHosts(orphan);
        var manager = scope.CreateManager();
        var recovery = manager.RecoverIfNeeded();
        var text = manager.ReadText();

        var passed = recovery.Performed
            && !recovery.FromJournal
            && !manager.ContainsBlockSection(text)
            && text.Contains("127.0.0.1 localhost", StringComparison.Ordinal)
            && text.Contains("# sample hosts", StringComparison.Ordinal);

        return (passed, $"performed={recovery.Performed}; fromJournal={recovery.FromJournal}; markersGone={!manager.ContainsBlockSection(text)}");
    }

    private static (bool, string) CrashRecoveryViaJournal(TempScope scope)
    {
        var original = Encoding.Latin1.GetBytes(SampleHosts);
        File.WriteAllBytes(scope.HostsPath, original);
        var manager = scope.CreateManager();
        manager.Apply(["example.com"]);

        var recovered = scope.CreateManager().RecoverIfNeeded();
        var final = scope.ReadHosts();
        var journalPath = Path.Combine(scope.BackupDir, "journal.json");

        var passed = recovered.Performed
            && recovered.FromJournal
            && final.AsSpan().SequenceEqual(original)
            && !File.Exists(journalPath);

        return (passed, $"performed={recovered.Performed}; fromJournal={recovered.FromJournal}; bytesEqual={final.AsSpan().SequenceEqual(original)}; journalRemoved={!File.Exists(journalPath)}");
    }

    private static (bool, string) InvalidDomainsRejected(TempScope scope)
    {
        scope.WriteHosts(SampleHosts);
        var before = scope.ReadHosts();
        var manager = scope.CreateManager();
        var invalid = new[] { "http://example.com", "*.example.com", "example.com/path", "exa mple.com", "" };
        var rejected = 0;

        foreach (var domain in invalid)
        {
            try
            {
                manager.Apply([domain]);
            }
            catch (ArgumentException)
            {
                rejected++;
            }
        }

        var after = scope.ReadHosts();
        var passed = rejected == invalid.Length && before.AsSpan().SequenceEqual(after);
        return (passed, $"rejected={rejected}/{invalid.Length}; hostsUnchanged={before.AsSpan().SequenceEqual(after)}");
    }

    private sealed class TempScope : IDisposable
    {
        private readonly string _directory;

        public TempScope(string workRoot)
        {
            _directory = Path.Combine(workRoot, Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(_directory);
        }

        public string HostsPath => Path.Combine(_directory, "hosts");

        public string BackupDir => Path.Combine(_directory, "backup");

        public void WriteHosts(string text) =>
            File.WriteAllBytes(HostsPath, Encoding.Latin1.GetBytes(text));

        public byte[] ReadHosts() => File.ReadAllBytes(HostsPath);

        public HostsFileManager CreateManager() => new(HostsPath, BackupDir);

        public void Dispose()
        {
            try
            {
                if (Directory.Exists(_directory))
                    Directory.Delete(_directory, recursive: true);
            }
            catch (IOException) { }
            catch (UnauthorizedAccessException) { }
        }
    }
}
