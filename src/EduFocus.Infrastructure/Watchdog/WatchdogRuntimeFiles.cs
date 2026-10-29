using System.Globalization;
using System.Text;

namespace EduFocus.Infrastructure.Watchdog;

/// <summary>
/// Small atomic file helpers for the cross-process runtime folder. The app
/// writes the heartbeat; the watchdog writes its state, pid and stop markers.
/// Every read and delete is best-effort because a missing file simply means
/// "no signal yet".
/// </summary>
public sealed class WatchdogRuntimeFiles
{
    public const string HeartbeatFileName = "app-heartbeat.json";
    public const string StateFileName = "watchdog-state.json";
    public const string PidFileName = "watchdog.pid";
    public const string StopFileName = "watchdog.stop";
    public const string ShutdownFileName = "shutdown.flag";

    private readonly WatchdogRuntimeOptions _options;

    public WatchdogRuntimeFiles(WatchdogRuntimeOptions options)
    {
        _options = options ?? throw new ArgumentNullException(nameof(options));
    }

    public string DirectoryPath => _options.RuntimeDirectory;

    public string HeartbeatPath => Path.Combine(DirectoryPath, HeartbeatFileName);

    public string StatePath => Path.Combine(DirectoryPath, StateFileName);

    public string PidPath => Path.Combine(DirectoryPath, PidFileName);

    public string StopRequestPath => Path.Combine(DirectoryPath, StopFileName);

    public string ShutdownFlagPath => Path.Combine(DirectoryPath, ShutdownFileName);

    public void EnsureDirectory() => Directory.CreateDirectory(DirectoryPath);

    public void WriteAtomic(string path, string content)
    {
        EnsureDirectory();
        var temporary = $"{path}.{Environment.ProcessId}.tmp";
        File.WriteAllText(temporary, content, Encoding.UTF8);
        File.Move(temporary, path, overwrite: true);
    }

    public string? TryReadAllText(string path)
    {
        try
        {
            return File.Exists(path) ? File.ReadAllText(path, Encoding.UTF8) : null;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            return null;
        }
    }

    public bool TryDelete(string path)
    {
        try
        {
            if (!File.Exists(path))
                return false;

            File.Delete(path);
            return true;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            return false;
        }
    }

    public void RequestStop() => WriteAtomic(StopRequestPath, Environment.ProcessId.ToString(CultureInfo.InvariantCulture));

    public bool HasStopRequest() => File.Exists(StopRequestPath);

    public void WriteWatchdogPid(int processId) =>
        WriteAtomic(PidPath, processId.ToString(CultureInfo.InvariantCulture));

    public void DeleteWatchdogPid() => TryDelete(PidPath);

    /// <summary>
    /// Records the current boot instant when Windows starts shutting down. A
    /// later watchdog start only treats the flag as valid while the machine is
    /// still on that boot; after a reboot the computed boot instant differs, so
    /// a stale flag is discarded and the watchdog proceeds.
    /// </summary>
    public void MarkShutdownInProgress() =>
        WriteAtomic(
            ShutdownFlagPath,
            (DateTime.UtcNow - TimeSpan.FromMilliseconds(Environment.TickCount64))
                .ToString("O", CultureInfo.InvariantCulture));

    public bool IsShutdownInProgressForCurrentBoot()
    {
        var text = TryReadAllText(ShutdownFlagPath);
        if (text is null)
            return false;

        if (!DateTime.TryParse(
                text.Trim(),
                CultureInfo.InvariantCulture,
                DateTimeStyles.RoundtripKind,
                out var savedBootUtc))
        {
            TryDelete(ShutdownFlagPath);
            return false;
        }

        var currentBootUtc = DateTime.UtcNow - TimeSpan.FromMilliseconds(Environment.TickCount64);

        if ((currentBootUtc - savedBootUtc.ToUniversalTime()).Duration() <= TimeSpan.FromSeconds(30))
            return true;

        TryDelete(ShutdownFlagPath);
        return false;
    }

    public void ClearShutdownFlag() => TryDelete(ShutdownFlagPath);

    public void DeleteAll()
    {
        TryDelete(HeartbeatPath);
        TryDelete(StatePath);
        TryDelete(PidPath);
        TryDelete(StopRequestPath);
        TryDelete(ShutdownFlagPath);
    }
}
