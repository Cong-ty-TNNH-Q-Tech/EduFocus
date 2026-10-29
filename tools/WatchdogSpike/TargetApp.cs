using System.Diagnostics;
using System.Text.Json;

namespace WatchdogSpike;

public static class TargetApp
{
    public static int Run(CliOptions options)
    {
        var paths = SpikePaths.Create(options.WorkDir);
        using var mutex = ProcessUtil.TryAcquireMutex("target", paths.Root, out var createdNew);
        if (!createdNew || mutex is null)
        {
            AuditLog.Append(paths.AuditPath, "target_duplicate", Environment.ProcessId,
                detail: "Một target khác đang chạy trong cùng work dir.");
            Console.Error.WriteLine("Target already running.");
            return 4;
        }

        using var self = Process.GetCurrentProcess();
        ProcessUtil.WriteRecord(paths.TargetPidPath, self);
        AuditLog.Append(paths.AuditPath, "target_process_start", self.Id);
        Console.WriteLine($"Target started pid={self.Id}");
        WriteHeartbeat(paths);

        var frozen = false;
        try
        {
            while (true)
            {
                if (File.Exists(paths.StopTargetPath))
                {
                    AtomicFile.WriteAllText(paths.CleanExitPath, DateTime.UtcNow.ToString("O"));
                    AuditLog.Append(paths.AuditPath, "target_clean_exit", self.Id);
                    Console.WriteLine("Target clean exit.");
                    break;
                }

                if (!frozen && File.Exists(paths.FreezeOncePath))
                {
                    try
                    {
                        File.Delete(paths.FreezeOncePath);
                    }
                    catch (IOException)
                    {
                    }

                    frozen = true;
                    AuditLog.Append(paths.AuditPath, "target_frozen", self.Id,
                        detail: "Heartbeat dừng nhưng process vẫn sống.");
                    Console.WriteLine("Target frozen.");
                }

                if (!frozen)
                    WriteHeartbeat(paths);

                Thread.Sleep(options.HeartbeatMs);
            }
        }
        finally
        {
            DeleteOwnPidFile(paths, self.Id);
            AuditLog.Append(paths.AuditPath, "target_exit", self.Id);
        }

        return 0;
    }

    private static void WriteHeartbeat(SpikePaths paths)
    {
        var record = new HeartbeatRecord(Environment.ProcessId, DateTime.UtcNow);
        AtomicFile.WriteAllText(paths.HeartbeatPath, JsonSerializer.Serialize(record, SpikeJson.Default));
    }

    private static void DeleteOwnPidFile(SpikePaths paths, int pid)
    {
        try
        {
            var record = ProcessUtil.ReadRecord(paths.TargetPidPath);
            if (record?.Pid == pid)
                File.Delete(paths.TargetPidPath);
        }
        catch (IOException)
        {
        }
    }
}
