using System.Diagnostics;
using System.Text.Json;
using WatchdogSpike.Reporting;

namespace WatchdogSpike;

public static class SpikeScenario
{
    public static int Run(CliOptions options)
    {
        var startedAt = DateTimeOffset.Now;
        var runId = startedAt.ToString("yyyyMMdd-HHmmss");
        var workRoot = Path.Combine(Path.GetTempPath(), "edufocus-watchdog-spike", runId);
        Directory.CreateDirectory(workRoot);

        var report = new WatchdogReport
        {
            StartedAt = startedAt,
            OsDescription = System.Runtime.InteropServices.RuntimeInformation.OSDescription,
            FrameworkDescription = System.Runtime.InteropServices.RuntimeInformation.FrameworkDescription,
            Elevated = ProcessUtil.IsElevated(),
            UserName = Environment.UserName,
            WorkRoot = workRoot,
            Config = new WatchdogConfig(
                options.PollMs,
                options.RestartBaseMs,
                options.RestartMaxMs,
                options.MaxRestarts,
                options.ResetSeconds,
                options.StaleSeconds,
                options.TaskIntervalSeconds)
        };

        var phases = new List<PhaseResult>();
        Process? taskLoop = null;

        try
        {
            phases.Add(RunRestartPhase(Path.Combine(workRoot, "01-restarts"), options));
            phases.Add(RunHangPhase(Path.Combine(workRoot, "02-hang"), options));
            phases.Add(RunCleanExitPhase(Path.Combine(workRoot, "03-clean-exit"), options));
            phases.Add(RunUninstallPhase(Path.Combine(workRoot, "04-uninstall"), options));
            phases.Add(RunBudgetPhase(Path.Combine(workRoot, "05-budget"), options));
            phases.Add(RunWatchdogKillPhase(Path.Combine(workRoot, "06-watchdog-kill"), options));

            if (!options.SkipTaskSimulation)
            {
                var taskPhase = RunTaskSimulationPhase(Path.Combine(workRoot, "07-task-simulation"), options,
                    process => taskLoop = process);
                phases.Add(taskPhase);
            }
        }
        finally
        {
            try
            {
                if (taskLoop is not null && !taskLoop.HasExited)
                {
                    taskLoop.Kill(entireProcessTree: true);
                    taskLoop.WaitForExit(3000);
                }
            }
            catch (InvalidOperationException)
            {
            }

            report.FinishedAt = DateTimeOffset.Now;
            report.Phases.AddRange(phases);
        }

        report.ConcludedAt = DateTimeOffset.Now;
        report.AllPassed = phases.Count > 0 && phases.All(phase => phase.Passed);
        var writer = new WatchdogReportWriter();
        var paths = writer.Write(report, Path.GetFullPath(options.OutputDir));

        Console.WriteLine();
        Console.WriteLine($"Phases: {phases.Count(phase => phase.Passed)}/{phases.Count} đạt.");
        Console.WriteLine($"Report: {paths.MarkdownPath}");
        Console.WriteLine($"JSON:   {paths.JsonPath}");

        if (!options.KeepWorkDir && report.AllPassed)
        {
            try
            {
                Directory.Delete(workRoot, recursive: true);
            }
            catch (IOException)
            {
            }
        }

        return report.AllPassed ? 0 : 1;
    }

    private static PhaseResult RunRestartPhase(string dir, CliOptions options)
    {
        var paths = Prepare(dir);
        var metrics = new Dictionary<string, string>();
        var watchdog = StartWatchdog(paths, options, maxRestarts: 5);
        var target = WaitForTarget(paths, TimeSpan.FromSeconds(10));
        if (target is null)
            return Fail("restart + duplicate", paths, "Target không khởi động trong 10 giây.");

        var duplicateWatchdog = StartWatchdog(paths, options, maxRestarts: 5);
        var duplicateExited = duplicateWatchdog.WaitForExit(5000);
        var duplicateCode = duplicateExited ? duplicateWatchdog.ExitCode : -1;
        metrics["duplicate watchdog exit"] = duplicateCode.ToString();

        var duplicateTarget = StartTarget(paths, options);
        var duplicateTargetExited = duplicateTarget.WaitForExit(5000);
        var duplicateTargetCode = duplicateTargetExited ? duplicateTarget.ExitCode : -1;
        var originalStillAlive = ProcessUtil.IsAlive(target);
        metrics["duplicate target exit"] = duplicateTargetCode.ToString();
        metrics["original target vẫn sống"] = originalStillAlive.ToString();

        var latencies = new List<long>();
        for (var kill = 1; kill <= 3; kill++)
        {
            var previous = ProcessUtil.ReadRecord(paths.TargetPidPath);
            if (previous is null || !ProcessUtil.IsAlive(previous))
                return Fail("restart + duplicate", paths, $"Không thấy target trước lần kill {kill}.");

            var stopwatch = Stopwatch.StartNew();
            ProcessUtil.KillIfAlive(previous);
            var next = WaitForTarget(paths, TimeSpan.FromSeconds(15), previous.Pid);
            stopwatch.Stop();
            if (next is null)
                return Fail("restart + duplicate", paths, $"Không thấy target mới sau lần kill {kill}.");
            latencies.Add(stopwatch.ElapsedMilliseconds);
            metrics[$"kill {kill} → restart"] = $"{stopwatch.ElapsedMilliseconds} ms";
        }

        metrics["restart events"] = Count(paths, "restart").ToString();
        metrics["unexpected exits"] = Count(paths, "target_exit_unexpected").ToString();
        metrics["duplicate watchdog events"] = Count(paths, "watchdog_duplicate").ToString();
        metrics["duplicate target events"] = Count(paths, "target_duplicate").ToString();

        StopWatchdog(paths);
        Thread.Sleep(500);
        var targetStopped = !ProcessUtil.IsAlive(ProcessUtil.ReadRecord(paths.TargetPidPath));
        metrics["target dừng sau stop"] = targetStopped.ToString();

        var backoffGrew = latencies.Count == 3 &&
                          latencies[1] > latencies[0] &&
                          latencies[2] > latencies[1];
        metrics["backoff tăng dần"] = backoffGrew.ToString();
        metrics["watchdog exit"] = watchdog.HasExited ? watchdog.ExitCode.ToString() : "running";

        var passed = duplicateCode == 2 &&
                     duplicateTargetCode == 4 &&
                     originalStillAlive &&
                     latencies.Count == 3 &&
                     backoffGrew &&
                     targetStopped;
        return Result("restart + duplicate", paths, passed,
            "Watchdog restart target sau kill, chặn trùng instance và backoff tăng dần.",
            metrics);
    }

    private static PhaseResult RunHangPhase(string dir, CliOptions options)
    {
        var paths = Prepare(dir);
        var metrics = new Dictionary<string, string>();
        StartWatchdog(paths, options, maxRestarts: 5);
        var target = WaitForTarget(paths, TimeSpan.FromSeconds(10));
        if (target is null)
            return Fail("hang detection", paths, "Target không khởi động.");

        AtomicFile.WriteAllText(paths.FreezeOncePath, DateTime.UtcNow.ToString("O"));
        var stopwatch = Stopwatch.StartNew();
        var next = WaitForTarget(paths, TimeSpan.FromSeconds(15), target.Pid);
        stopwatch.Stop();
        metrics["freeze → target mới"] = $"{stopwatch.ElapsedMilliseconds} ms";
        metrics["hang events"] = Count(paths, "hang_detected").ToString();
        metrics["restart events"] = Count(paths, "restart").ToString();

        StopWatchdog(paths);
        var passed = next is not null &&
                     stopwatch.ElapsedMilliseconds < 12000 &&
                     Count(paths, "hang_detected") >= 1;
        return Result("hang detection", paths, passed,
            "Watchdog phát hiện heartbeat cũ, kill target treo và khởi động lại.",
            metrics);
    }

    private static PhaseResult RunCleanExitPhase(string dir, CliOptions options)
    {
        var paths = Prepare(dir);
        var metrics = new Dictionary<string, string>();
        var watchdog = StartWatchdog(paths, options, maxRestarts: 5);
        var target = WaitForTarget(paths, TimeSpan.FromSeconds(10));
        if (target is null)
            return Fail("clean exit", paths, "Target không khởi động.");

        AtomicFile.WriteAllText(paths.StopTargetPath, "clean-exit");
        var stopwatch = Stopwatch.StartNew();
        var exited = watchdog.WaitForExit(10000);
        stopwatch.Stop();
        var code = exited ? watchdog.ExitCode : -1;
        metrics["watchdog exit"] = code.ToString();
        metrics["stop → watchdog exit"] = $"{stopwatch.ElapsedMilliseconds} ms";
        Thread.Sleep(1500);
        var targetAlive = ProcessUtil.IsAlive(ProcessUtil.ReadRecord(paths.TargetPidPath));
        metrics["target còn sống"] = targetAlive.ToString();
        metrics["clean exit events"] = Count(paths, "clean_exit_detected").ToString();
        metrics["restart events"] = Count(paths, "restart").ToString();
        metrics["clean-exit.flag"] = File.Exists(paths.CleanExitPath).ToString();

        var passed = code == 0 && !targetAlive && File.Exists(paths.CleanExitPath);
        return Result("clean exit", paths, passed,
            "Target thoát êm; watchdog dừng và không restart.",
            metrics);
    }

    private static PhaseResult RunUninstallPhase(string dir, CliOptions options)
    {
        var paths = Prepare(dir);
        var metrics = new Dictionary<string, string>();
        var watchdog = StartWatchdog(paths, options, maxRestarts: 5);
        var target = WaitForTarget(paths, TimeSpan.FromSeconds(10));
        if (target is null)
            return Fail("uninstall stop", paths, "Target không khởi động.");

        ProcessUtil.KillIfAlive(target);
        AtomicFile.WriteAllText(paths.StopWatchdogPath, "uninstall");
        var exited = watchdog.WaitForExit(10000);
        var code = exited ? watchdog.ExitCode : -1;
        metrics["watchdog exit"] = code.ToString();
        Thread.Sleep(2500);
        var newTarget = ProcessUtil.ReadRecord(paths.TargetPidPath);
        var targetAlive = ProcessUtil.IsAlive(newTarget);
        metrics["target mới sau stop"] = targetAlive.ToString();
        metrics["restart events"] = Count(paths, "restart").ToString();
        metrics["stop events"] = Count(paths, "watchdog_stopped").ToString();

        var passed = code == 0 && !targetAlive && Count(paths, "restart") == 0;
        return Result("uninstall stop", paths, passed,
            "Dừng watchdog trước backoff: không restart, exit sạch.",
            metrics);
    }

    private static PhaseResult RunBudgetPhase(string dir, CliOptions options)
    {
        var paths = Prepare(dir);
        var metrics = new Dictionary<string, string>();
        var watchdog = StartWatchdog(paths, options, maxRestarts: 2, restartBaseMs: 500);
        if (WaitForTarget(paths, TimeSpan.FromSeconds(10)) is null)
            return Fail("restart budget", paths, "Target không khởi động.");

        long lastLatency = 0;
        for (var kill = 1; kill <= 2; kill++)
        {
            var previous = ProcessUtil.ReadRecord(paths.TargetPidPath);
            var stopwatch = Stopwatch.StartNew();
            ProcessUtil.KillIfAlive(previous);
            var next = WaitForTarget(paths, TimeSpan.FromSeconds(12), previous!.Pid);
            stopwatch.Stop();
            if (next is null)
                return Fail("restart budget", paths, $"Không thấy target mới sau kill {kill}.");
            lastLatency = stopwatch.ElapsedMilliseconds;
            metrics[$"kill {kill} → restart"] = $"{stopwatch.ElapsedMilliseconds} ms";
        }

        var last = ProcessUtil.ReadRecord(paths.TargetPidPath);
        ProcessUtil.KillIfAlive(last);
        var exited = watchdog.WaitForExit(12000);
        var code = exited ? watchdog.ExitCode : -1;
        metrics["watchdog exit"] = code.ToString();
        metrics["give up events"] = Count(paths, "give_up").ToString();
        Thread.Sleep(1500);
        var targetAlive = ProcessUtil.IsAlive(ProcessUtil.ReadRecord(paths.TargetPidPath));
        metrics["target sau give up"] = targetAlive.ToString();
        metrics["restart cuối"] = $"{lastLatency} ms";

        var passed = code == 3 && !targetAlive && Count(paths, "give_up") >= 1;
        return Result("restart budget", paths, passed,
            "Sau 2 restart trong cửa sổ, watchdog bỏ cuộc và không restart vô hạn.",
            metrics);
    }

    private static PhaseResult RunWatchdogKillPhase(string dir, CliOptions options)
    {
        var paths = Prepare(dir);
        var metrics = new Dictionary<string, string>();
        var watchdog = StartWatchdog(paths, options, maxRestarts: 5);
        var target = WaitForTarget(paths, TimeSpan.FromSeconds(10));
        if (target is null)
            return Fail("watchdog kill (bypass)", paths, "Target không khởi động.");

        var watchdogRecord = ProcessUtil.ReadRecord(paths.WatchdogPidPath);
        ProcessUtil.KillIfAlive(watchdogRecord, entireTree: false);
        watchdog.WaitForExit(3000);
        metrics["watchdog killed"] = (!ProcessUtil.IsAlive(watchdogRecord)).ToString();

        Thread.Sleep(1500);
        var targetSurvived = ProcessUtil.IsAlive(target);
        metrics["target sống sau khi watchdog chết"] = targetSurvived.ToString();

        ProcessUtil.KillIfAlive(target);
        Thread.Sleep(3000);
        var newTarget = ProcessUtil.ReadRecord(paths.TargetPidPath);
        var restarted = ProcessUtil.IsAlive(newTarget) && (newTarget?.Pid != target.Pid);
        metrics["restart sau khi cả hai chết"] = restarted.ToString();
        metrics["restart events"] = Count(paths, "restart").ToString();

        var passed = !ProcessUtil.IsAlive(watchdogRecord) && targetSurvived && !restarted;
        return Result("watchdog kill (bypass)", paths, passed,
            "Kill watchdog: target vẫn chạy nhưng không có gì restart target sau đó — cần lớp ngoài (scheduled task/service).",
            metrics);
    }

    private static PhaseResult RunTaskSimulationPhase(string dir, CliOptions options, Action<Process> track)
    {
        var paths = Prepare(dir);
        var metrics = new Dictionary<string, string> { ["registered task"] = "false (mô phỏng)" };
        var taskLoop = ProcessUtil.StartSelf(
        [
            "task-check-loop",
            "--work-dir", paths.Root,
            "--heartbeat-ms", options.HeartbeatMs.ToString(),
            "--task-interval-seconds", options.TaskIntervalSeconds.ToString(),
            "--stale-seconds", options.StaleSeconds.ToString()
        ]);
        track(taskLoop);

        var initial = WaitForTarget(paths, TimeSpan.FromSeconds(10));
        if (initial is null)
            return Fail("scheduled task (simulation)", paths, "Task-check không khởi động target.");

        var stopwatch = Stopwatch.StartNew();
        ProcessUtil.KillIfAlive(initial);
        var next = WaitForTarget(paths, TimeSpan.FromSeconds(options.TaskIntervalSeconds + 20), initial.Pid);
        stopwatch.Stop();
        metrics["kill → task restart"] = $"{stopwatch.ElapsedMilliseconds} ms";
        metrics["task restart events"] = Count(paths, "task_restart").ToString();
        metrics["interval"] = $"{options.TaskIntervalSeconds} giây";

        AtomicFile.WriteAllText(paths.StopWatchdogPath, "task-simulation-end");
        taskLoop.WaitForExit(5000);
        if (!taskLoop.HasExited)
            taskLoop.Kill(entireProcessTree: true);

        ProcessUtil.KillIfAlive(ProcessUtil.ReadRecord(paths.TargetPidPath));

        var passed = next is not null &&
                     Count(paths, "task_restart") >= 1 &&
                     stopwatch.ElapsedMilliseconds < (options.TaskIntervalSeconds + 15) * 1000;
        return Result("scheduled task (simulation)", paths, passed,
            "Mô phỏng chu kỳ scheduled task: phát hiện target chết và restart trong một chu kỳ.",
            metrics);
    }

    private static SpikePaths Prepare(string dir)
    {
        var paths = SpikePaths.Create(dir);
        SpikeStateStore.Write(paths.StatePath, new SpikeState(true, null));
        foreach (var file in new[]
                 {
                     paths.StopWatchdogPath, paths.StopTargetPath, paths.CleanExitPath, paths.FreezeOncePath
                 })
        {
            try
            {
                if (File.Exists(file))
                    File.Delete(file);
            }
            catch (IOException)
            {
            }
        }

        return paths;
    }

    private static Process StartWatchdog(SpikePaths paths, CliOptions options, int? maxRestarts = null,
        int? restartBaseMs = null)
    {
        return ProcessUtil.StartSelf(
        [
            "watchdog",
            "--work-dir", paths.Root,
            "--poll-ms", options.PollMs.ToString(),
            "--restart-base-ms", (restartBaseMs ?? options.RestartBaseMs).ToString(),
            "--restart-max-ms", options.RestartMaxMs.ToString(),
            "--max-restarts", (maxRestarts ?? options.MaxRestarts).ToString(),
            "--reset-seconds", options.ResetSeconds.ToString(),
            "--stale-seconds", options.StaleSeconds.ToString(),
            "--graceful-stop-ms", options.GracefulStopMs.ToString(),
            "--heartbeat-ms", options.HeartbeatMs.ToString()
        ]);
    }

    private static Process StartTarget(SpikePaths paths, CliOptions options) =>
        ProcessUtil.StartSelf(
        [
            "target",
            "--work-dir", paths.Root,
            "--heartbeat-ms", options.HeartbeatMs.ToString()
        ]);

    private static ProcessRecord? WaitForTarget(SpikePaths paths, TimeSpan timeout, int? excludePid = null)
    {
        var stopwatch = Stopwatch.StartNew();
        while (stopwatch.Elapsed < timeout)
        {
            var record = ProcessUtil.ReadRecord(paths.TargetPidPath);
            if (record is not null &&
                record.Pid != excludePid &&
                ProcessUtil.IsAlive(record) &&
                HeartbeatFresh(paths, record))
                return record;
            Thread.Sleep(100);
        }

        return null;
    }

    private static bool HeartbeatFresh(SpikePaths paths, ProcessRecord record)
    {
        try
        {
            if (!File.Exists(paths.HeartbeatPath))
                return false;
            var heartbeat = JsonSerializer.Deserialize<HeartbeatRecord>(
                File.ReadAllText(paths.HeartbeatPath), SpikeJson.Default);
            return heartbeat is not null &&
                   heartbeat.Pid == record.Pid &&
                   DateTime.UtcNow - heartbeat.UpdatedAtUtc < TimeSpan.FromSeconds(2);
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

    private static void StopWatchdog(SpikePaths paths)
    {
        AtomicFile.WriteAllText(paths.StopWatchdogPath, "scenario-stop");
        var watchdog = ProcessUtil.ReadRecord(paths.WatchdogPidPath);
        var deadline = DateTime.UtcNow.AddSeconds(8);
        while (DateTime.UtcNow < deadline && ProcessUtil.IsAlive(watchdog))
            Thread.Sleep(100);
        ProcessUtil.KillIfAlive(watchdog);
        var target = ProcessUtil.ReadRecord(paths.TargetPidPath);
        ProcessUtil.KillIfAlive(target);
    }

    private static int Count(SpikePaths paths, string eventName) =>
        AuditLog.Read(paths.AuditPath).Count(item => item.Event == eventName);

    private static PhaseResult Result(string name, SpikePaths paths, bool passed, string summary,
        IReadOnlyDictionary<string, string> metrics) =>
        new(name, passed, summary, metrics, AuditLog.Read(paths.AuditPath), paths.Root);

    private static PhaseResult Fail(string name, SpikePaths paths, string detail) =>
        new(name, false, detail, new Dictionary<string, string>(), AuditLog.Read(paths.AuditPath), paths.Root);
}
