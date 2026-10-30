using System.Diagnostics;
using System.Text.Json;

namespace WatchdogSpike;

public static class WatchdogRunner
{
    public static int Run(CliOptions options)
    {
        var paths = SpikePaths.Create(options.WorkDir);
        using var mutex = ProcessUtil.TryAcquireMutex("watchdog", paths.Root, out var createdNew);
        if (!createdNew || mutex is null)
        {
            AuditLog.Append(paths.AuditPath, "watchdog_duplicate", Environment.ProcessId);
            Console.Error.WriteLine("Watchdog already running.");
            return 2;
        }

        using var self = Process.GetCurrentProcess();
        ProcessUtil.WriteRecord(paths.WatchdogPidPath, self);
        AuditLog.Append(paths.AuditPath, "watchdog_start", self.Id, detail: options.WorkDir);
        Console.WriteLine($"Watchdog started pid={self.Id}");

        ProcessRecord? target = null;
        var everStarted = false;
        var restartCount = 0;
        var lastRestartUtc = DateTime.MinValue;
        var lastExitWasHang = false;

        try
        {
            while (true)
            {
                if (File.Exists(paths.StopWatchdogPath))
                {
                    StopTargetGracefully(paths, options, target, "stop-watchdog");
                    AuditLog.Append(paths.AuditPath, "watchdog_stopped", self.Id, detail: "stop-watchdog.flag");
                    return 0;
                }

                var state = SpikeStateStore.Read(paths.StatePath);
                if (!state.ProtectionEnabled)
                {
                    StopTargetGracefully(paths, options, target, "protection-off");
                    AuditLog.Append(paths.AuditPath, "protection_off", self.Id);
                    return 0;
                }

                if (target is null && !everStarted)
                {
                    var existing = ProcessUtil.ReadRecord(paths.TargetPidPath);
                    if (ProcessUtil.IsAlive(existing))
                    {
                        target = existing;
                        everStarted = true;
                        AuditLog.Append(paths.AuditPath, "target_adopted", existing!.Pid);
                    }
                }

                if (ProcessUtil.IsAlive(target))
                {
                    if (IsHeartbeatStale(paths, target!, options.StaleSeconds))
                    {
                        AuditLog.Append(paths.AuditPath, "hang_detected", target!.Pid,
                            detail: $"Heartbeat cũ hơn {options.StaleSeconds} giây.");
                        ProcessUtil.KillIfAlive(target);
                        target = null;
                        lastExitWasHang = true;
                        continue;
                    }

                    if (restartCount > 0 &&
                        DateTime.UtcNow - lastRestartUtc > TimeSpan.FromSeconds(options.ResetSeconds))
                    {
                        restartCount = 0;
                        AuditLog.Append(paths.AuditPath, "backoff_reset", self.Id,
                            detail: $"Target ổn định > {options.ResetSeconds} giây.");
                    }

                    Thread.Sleep(options.PollMs);
                    continue;
                }

                var deadPid = target?.Pid;
                var cleanExit = File.Exists(paths.CleanExitPath);
                if (deadPid is not null && !lastExitWasHang && !cleanExit)
                    AuditLog.Append(paths.AuditPath, "target_exit_unexpected", deadPid,
                        attempt: restartCount + 1);

                target = null;
                lastExitWasHang = false;

                if (cleanExit)
                {
                    AuditLog.Append(paths.AuditPath, "clean_exit_detected", self.Id);
                    return 0;
                }

                if (restartCount >= options.MaxRestarts)
                {
                    AuditLog.Append(paths.AuditPath, "give_up", self.Id, attempt: restartCount,
                        detail: $"Đã restart {restartCount} lần trong cửa sổ {options.ResetSeconds} giây.");
                    return 3;
                }

                var delayMs = everStarted
                    ? Math.Min(options.RestartBaseMs * (1 << Math.Min(restartCount, 10)), options.RestartMaxMs)
                    : 0;

                if (delayMs > 0 && WaitOrStop(paths, delayMs))
                {
                    StopTargetGracefully(paths, options, null, "stop-during-backoff");
                    AuditLog.Append(paths.AuditPath, "watchdog_stopped", self.Id,
                        detail: "Dừng trong thời gian backoff.");
                    return 0;
                }

                var child = ProcessUtil.StartSelf(TargetArguments(paths.Root, options.HeartbeatMs));
                ProcessUtil.WriteRecord(paths.TargetPidPath, child);
                target = new ProcessRecord(child.Id, child.StartTime.ToUniversalTime());
                AuditLog.Append(paths.AuditPath, everStarted ? "restart" : "target_start", child.Id,
                    attempt: everStarted ? restartCount + 1 : null,
                    delayMs: everStarted ? delayMs : null);
                if (everStarted)
                {
                    restartCount++;
                    lastRestartUtc = DateTime.UtcNow;
                }

                everStarted = true;
                Thread.Sleep(options.PollMs);
            }
        }
        catch (Exception ex)
        {
            AuditLog.Append(paths.AuditPath, "watchdog_error", self.Id,
                detail: $"{ex.GetType().Name}: {ex.Message}");
            return 1;
        }
        finally
        {
            try
            {
                var record = ProcessUtil.ReadRecord(paths.WatchdogPidPath);
                if (record?.Pid == self.Id)
                    File.Delete(paths.WatchdogPidPath);
            }
            catch (IOException)
            {
            }
        }
    }

    private static IReadOnlyList<string> TargetArguments(string workDir, int heartbeatMs) =>
    [
        "target",
        "--work-dir", workDir,
        "--heartbeat-ms", heartbeatMs.ToString()
    ];

    private static bool WaitOrStop(SpikePaths paths, int delayMs)
    {
        var deadline = DateTime.UtcNow.AddMilliseconds(delayMs);
        while (DateTime.UtcNow < deadline)
        {
            if (File.Exists(paths.StopWatchdogPath) ||
                !SpikeStateStore.Read(paths.StatePath).ProtectionEnabled)
                return true;
            Thread.Sleep(Math.Min(100, Math.Max(20, delayMs)));
        }

        return false;
    }

    private static bool IsHeartbeatStale(SpikePaths paths, ProcessRecord target, int staleSeconds)
    {
        try
        {
            if (!File.Exists(paths.HeartbeatPath))
                return false;
            var heartbeat = JsonSerializer.Deserialize<HeartbeatRecord>(
                File.ReadAllText(paths.HeartbeatPath), SpikeJson.Default);
            if (heartbeat is null || heartbeat.Pid != target.Pid)
                return false;
            return DateTime.UtcNow - heartbeat.UpdatedAtUtc > TimeSpan.FromSeconds(staleSeconds);
        }
        catch (IOException)
        {
            return false;
        }
        catch (JsonException)
        {
            return false;
        }
    }

    private static void StopTargetGracefully(SpikePaths paths, CliOptions options, ProcessRecord? target, string reason)
    {
        var record = target ?? ProcessUtil.ReadRecord(paths.TargetPidPath);
        if (!ProcessUtil.IsAlive(record))
            return;

        AtomicFile.WriteAllText(paths.StopTargetPath, reason);
        var deadline = DateTime.UtcNow.AddMilliseconds(options.GracefulStopMs);
        while (DateTime.UtcNow < deadline && ProcessUtil.IsAlive(record))
            Thread.Sleep(100);

        if (ProcessUtil.IsAlive(record))
        {
            ProcessUtil.KillIfAlive(record);
            AuditLog.Append(paths.AuditPath, "target_killed_on_stop", record!.Pid, detail: reason);
        }
    }
}
