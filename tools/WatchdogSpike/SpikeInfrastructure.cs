using System.Diagnostics;
using System.Reflection;
using System.Security.Cryptography;
using System.Security.Principal;
using System.Text;
using System.Text.Json;

namespace WatchdogSpike;

public sealed class SpikePaths
{
    private SpikePaths(string root)
    {
        Root = root;
        Directory.CreateDirectory(root);
    }

    public string Root { get; }
    public string StatePath => Path.Combine(Root, "state.json");
    public string TargetPidPath => Path.Combine(Root, "target.pid");
    public string WatchdogPidPath => Path.Combine(Root, "watchdog.pid");
    public string HeartbeatPath => Path.Combine(Root, "heartbeat.json");
    public string AuditPath => Path.Combine(Root, "audit.jsonl");
    public string StopWatchdogPath => Path.Combine(Root, "stop-watchdog.flag");
    public string StopTargetPath => Path.Combine(Root, "stop-target.flag");
    public string CleanExitPath => Path.Combine(Root, "clean-exit.flag");
    public string FreezeOncePath => Path.Combine(Root, "freeze-once.flag");

    public static SpikePaths Create(string root) => new(Path.GetFullPath(root));
}

public sealed record ProcessRecord(int Pid, DateTime StartTimeUtc);

public sealed record HeartbeatRecord(int Pid, DateTime UpdatedAtUtc);

public sealed record SpikeState(bool ProtectionEnabled, DateTimeOffset? LastCleanShutdownAtUtc);

public sealed record AuditEvent(
    DateTime TimestampUtc,
    string Event,
    int? Pid = null,
    int? Attempt = null,
    int? DelayMs = null,
    string? Detail = null);

public static class SpikeStateStore
{
    

    public static SpikeState Read(string path)
    {
        try
        {
            return JsonSerializer.Deserialize<SpikeState>(File.ReadAllText(path), SpikeJson.Default)
                   ?? new SpikeState(true, null);
        }
        catch (IOException)
        {
            return new SpikeState(true, null);
        }
        catch (JsonException)
        {
            return new SpikeState(true, null);
        }
    }

    public static void Write(string path, SpikeState state) =>
        AtomicFile.WriteAllText(path, JsonSerializer.Serialize(state, SpikeJson.Default));
}

public static class AuditLog
{
    private static readonly object Sync = new();

    public static void Append(string path, string eventName, int? pid = null, int? attempt = null,
        int? delayMs = null, string? detail = null)
    {
        var record = new AuditEvent(DateTime.UtcNow, eventName, pid, attempt, delayMs, detail);
        var line = JsonSerializer.Serialize(record, SpikeJson.Default) + Environment.NewLine;
        lock (Sync)
        {
            try
            {
                using var stream = new FileStream(path, FileMode.Append, FileAccess.Write, FileShare.ReadWrite);
                using var writer = new StreamWriter(stream);
                writer.Write(line);
            }
            catch (IOException)
            {
                // Audit không được làm chết watchdog.
            }
            catch (UnauthorizedAccessException)
            {
            }
        }
    }

    public static IReadOnlyList<AuditEvent> Read(string path)
    {
        if (!File.Exists(path))
            return Array.Empty<AuditEvent>();

        var events = new List<AuditEvent>();
        try
        {
            using var stream = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite);
            using var reader = new StreamReader(stream);
            while (reader.ReadLine() is { } line)
            {
                if (string.IsNullOrWhiteSpace(line))
                    continue;
                try
                {
                    var record = JsonSerializer.Deserialize<AuditEvent>(line, SpikeJson.Default);
                    if (record is not null)
                        events.Add(record);
                }
                catch (JsonException)
                {
                }
            }
        }
        catch (IOException)
        {
        }

        return events;
    }
}

public static class AtomicFile
{
    public static void WriteAllText(string path, string content)
    {
        var directory = Path.GetDirectoryName(path);
        if (!string.IsNullOrEmpty(directory))
            Directory.CreateDirectory(directory);

        var temp = path + "." + Guid.NewGuid().ToString("N")[..8] + ".tmp";
        File.WriteAllText(temp, content);
        for (var attempt = 0; ; attempt++)
        {
            try
            {
                File.Move(temp, path, overwrite: true);
                return;
            }
            catch (IOException) when (attempt < 4)
            {
                Thread.Sleep(50);
            }
        }
    }
}

public static class ProcessUtil
{
    public static (string FileName, IReadOnlyList<string> PrefixArgs) SelfCommand()
    {
        var path = Environment.ProcessPath
                   ?? throw new InvalidOperationException("Không xác định được process path.");
        if (string.Equals(Path.GetFileNameWithoutExtension(path), "WatchdogSpike", StringComparison.OrdinalIgnoreCase))
            return (path, Array.Empty<string>());

        var dll = Assembly.GetEntryAssembly()?.Location;
        if (string.IsNullOrWhiteSpace(dll))
            throw new InvalidOperationException("Không xác định được assembly path.");
        return (path, new[] { dll });
    }

    public static Process StartSelf(IEnumerable<string> args)
    {
        var (fileName, prefix) = SelfCommand();
        var startInfo = new ProcessStartInfo
        {
            FileName = fileName,
            UseShellExecute = false,
            CreateNoWindow = true,
            RedirectStandardOutput = true,
            RedirectStandardError = true
        };
        foreach (var arg in prefix)
            startInfo.ArgumentList.Add(arg);
        foreach (var arg in args)
            startInfo.ArgumentList.Add(arg);

        var process = Process.Start(startInfo) ?? throw new InvalidOperationException("Không start được tiến trình.");
        process.OutputDataReceived += (_, _) => { };
        process.ErrorDataReceived += (_, _) => { };
        process.BeginOutputReadLine();
        process.BeginErrorReadLine();
        return process;
    }

    public static ProcessRecord? ReadRecord(string path)
    {
        try
        {
            if (!File.Exists(path))
                return null;
            return JsonSerializer.Deserialize<ProcessRecord>(File.ReadAllText(path), SpikeJson.Default);
        }
        catch (IOException)
        {
            return null;
        }
        catch (JsonException)
        {
            return null;
        }
    }

    public static void WriteRecord(string path, Process process)
    {
        var record = new ProcessRecord(process.Id, process.StartTime.ToUniversalTime());
        AtomicFile.WriteAllText(path, JsonSerializer.Serialize(record, SpikeJson.Default));
    }

    public static bool IsAlive(ProcessRecord? record)
    {
        if (record is null)
            return false;

        try
        {
            using var process = Process.GetProcessById(record.Pid);
            var start = process.StartTime.ToUniversalTime();
            return Math.Abs((start - record.StartTimeUtc).TotalSeconds) < 2;
        }
        catch (ArgumentException)
        {
            return false;
        }
        catch (InvalidOperationException)
        {
            return false;
        }
    }

    public static void KillIfAlive(ProcessRecord? record, bool entireTree = true)
    {
        if (record is null)
            return;
        try
        {
            using var process = Process.GetProcessById(record.Pid);
            if (!process.HasExited)
                process.Kill(entireProcessTree: entireTree);
            process.WaitForExit(3000);
        }
        catch (ArgumentException)
        {
        }
        catch (InvalidOperationException)
        {
        }
        catch (System.ComponentModel.Win32Exception)
        {
        }
    }

    public static Mutex? TryAcquireMutex(string role, string workDir, out bool createdNew)
    {
        var mutex = new Mutex(true, MutexName(role, workDir), out createdNew);
        if (createdNew)
            return mutex;

        mutex.Dispose();
        return null;
    }

    public static bool IsElevated()
    {
        try
        {
            using var identity = WindowsIdentity.GetCurrent();
            return new WindowsPrincipal(identity).IsInRole(WindowsBuiltInRole.Administrator);
        }
        catch (Exception)
        {
            return false;
        }
    }

    private static string MutexName(string role, string workDir)
    {
        var normalized = Path.GetFullPath(workDir).ToLowerInvariant();
        var hash = Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(normalized)))[..12];
        return $"Local\\EduFocus.WatchdogSpike.{role}.{hash}";
    }
}

internal static class SpikeJson
{
    public static readonly JsonSerializerOptions Default = new()
    {
        PropertyNameCaseInsensitive = true,
        WriteIndented = false
    };
}
