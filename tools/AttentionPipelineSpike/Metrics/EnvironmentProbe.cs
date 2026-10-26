using System.Runtime.InteropServices;
using Microsoft.Win32;

namespace AttentionPipelineSpike.Metrics;

public static class EnvironmentProbe
{
    public static EnvironmentInfo Capture()
    {
        return new EnvironmentInfo(
            OsDescription: RuntimeInformation.OSDescription,
            Framework: RuntimeInformation.FrameworkDescription,
            ProcessArchitecture: RuntimeInformation.ProcessArchitecture.ToString(),
            LogicalProcessors: Environment.ProcessorCount,
            CpuName: ReadCpuName(),
            ApproximatePhysicalMemoryBytes: GC.GetGCMemoryInfo().TotalAvailableMemoryBytes,
            StartedAtUtc: DateTimeOffset.UtcNow);
    }

    private static string ReadCpuName()
    {
        try
        {
            using var key = Registry.LocalMachine.OpenSubKey(
                @"HARDWARE\DESCRIPTION\System\CentralProcessor\0");
            return key?.GetValue("ProcessorNameString")?.ToString()?.Trim() ?? "unknown";
        }
        catch
        {
            return "unknown";
        }
    }
}
