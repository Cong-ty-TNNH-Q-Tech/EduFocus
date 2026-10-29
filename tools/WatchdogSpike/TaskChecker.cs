using System.Text.Json;

namespace WatchdogSpike;

public static class TaskChecker
{
    public static int Run(CliOptions options)
    {
        if (options.Command == SpikeCommand.TaskCheckLoop)
            return RunLoop(options);
        return RunOnce(options);
    }

    private static int RunLoop(CliOptions options)
    {
        var paths = SpikePaths.Create(options.WorkDir);
        AuditLog.Append(paths.AuditPath, "task_loop_start", Environment.ProcessId,
            detail: $"Mô phỏng scheduled task mỗi {options.TaskIntervalSeconds} giây.");
        Console.WriteLine($"Task-check loop started, interval={options.TaskIntervalSeconds}s");

        while (true)
        {
            if (ShouldStop(paths))
                break;

            RunOnce(options);

            if (WaitInterval(options, paths))
                break;
        }

        AuditLog.Append(paths.AuditPath, "task_loop_stopped", Environment.ProcessId);
        return 0;
    }

    private static bool WaitInterval(CliOptions options, SpikePaths paths)
    {
        var deadline = DateTime.UtcNow.AddSeconds(options.TaskIntervalSeconds);
        while (DateTime.UtcNow < deadline)
        {
            if (ShouldStop(paths))
                return true;
            Thread.Sleep(250);
        }

        return false;
    }

    private static bool ShouldStop(SpikePaths paths) =>
        File.Exists(paths.StopWatchdogPath) ||
        File.Exists(paths.CleanExitPath) ||
        !SpikeStateStore.Read(paths.StatePath).ProtectionEnabled;

    private static int RunOnce(CliOptions options)
    {
        var paths = SpikePaths.Create(options.WorkDir);
        if (ShouldStop(paths))
        {
            AuditLog.Append(paths.AuditPath, "task_check_skip", Environment.ProcessId);
            return 0;
        }

        using var mutex = ProcessUtil.TryAcquireMutex("task-check", paths.Root, out var createdNew);
        if (!createdNew || mutex is null)
        {
            AuditLog.Append(paths.AuditPath, "task_check_duplicate", Environment.ProcessId);
            return 0;
        }

        var target = ProcessUtil.ReadRecord(paths.TargetPidPath);
        if (ProcessUtil.IsAlive(target))
        {
            if (IsHeartbeatStale(paths, target!, options.StaleSeconds))
            {
                AuditLog.Append(paths.AuditPath, "task_hang_kill", target!.Pid,
                    detail: $"Heartbeat cũ hơn {options.StaleSeconds} giây.");
                ProcessUtil.KillIfAlive(target);
                return 0;
            }

            AuditLog.Append(paths.AuditPath, "task_check_ok", target!.Pid);
            return 0;
        }

        if (File.Exists(paths.CleanExitPath))
        {
            AuditLog.Append(paths.AuditPath, "task_check_clean_exit", Environment.ProcessId);
            return 0;
        }

        var child = ProcessUtil.StartSelf(
        [
            "target",
            "--work-dir", paths.Root,
            "--heartbeat-ms", options.HeartbeatMs.ToString()
        ]);
        ProcessUtil.WriteRecord(paths.TargetPidPath, child);
        AuditLog.Append(paths.AuditPath, "task_restart", child.Id,
            detail: "Scheduled task phát hiện target không chạy.");
        child.Dispose();
        return 0;
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
}
