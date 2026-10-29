using System.ComponentModel;
using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Runtime.Versioning;
using System.Text;
using EduFocus.Ports.Outbound.Services;

namespace EduFocus.Infrastructure.Activity;

/// <summary>
/// Reads the foreground window, its process and idle time (UC07, architecture
/// §7.1). Access-denied and disappeared processes yield null fields instead of
/// failing the sample; only the idle counter needs no extra permissions.
/// </summary>
[SupportedOSPlatform("windows")]
public sealed class WindowsForegroundActivityPort : IForegroundActivityPort
{
    private const int MaxTitleLength = 512;

    public ForegroundActivitySnapshot? GetForegroundActivity()
    {
        var handle = GetForegroundWindow();
        if (handle == IntPtr.Zero)
            return null;

        if (GetWindowThreadProcessId(handle, out var processId) == 0 || processId == 0)
            return null;

        var (processName, appName) = ReadProcess((int)processId);

        return new ForegroundActivitySnapshot(
            (int)processId,
            handle,
            processName,
            appName,
            ReadWindowTitle(handle),
            ReadIdleSeconds());
    }

    private static (string? ProcessName, string? AppName) ReadProcess(int processId)
    {
        try
        {
            using var process = Process.GetProcessById(processId);

            var processName = string.IsNullOrWhiteSpace(process.ProcessName)
                ? null
                : process.ProcessName;

            return (processName, ReadAppName(process));
        }
        catch (ArgumentException)
        {
            return (null, null);
        }
        catch (InvalidOperationException)
        {
            return (null, null);
        }
        catch (Win32Exception)
        {
            return (null, null);
        }
    }

    private static string? ReadAppName(Process process)
    {
        try
        {
            var description = process.MainModule?.FileVersionInfo.FileDescription;
            return string.IsNullOrWhiteSpace(description) ? null : description.Trim();
        }
        catch (Win32Exception)
        {
            return null;
        }
        catch (InvalidOperationException)
        {
            return null;
        }
        catch (NotSupportedException)
        {
            return null;
        }
    }

    private static string? ReadWindowTitle(IntPtr handle)
    {
        var length = GetWindowTextLengthW(handle);
        if (length <= 0)
            return null;

        var builder = new StringBuilder(Math.Min(length + 1, MaxTitleLength + 1));

        return GetWindowTextW(handle, builder, builder.Capacity) > 0
            ? builder.ToString()
            : null;
    }

    private static int ReadIdleSeconds()
    {
        var info = new LastInputInfo { Size = (uint)Marshal.SizeOf<LastInputInfo>() };
        if (!GetLastInputInfo(ref info))
            return 0;

        var idleMilliseconds = unchecked((uint)Environment.TickCount - info.Time);
        var seconds = idleMilliseconds / 1000u;

        return seconds > int.MaxValue ? int.MaxValue : (int)seconds;
    }

    [DllImport("user32.dll")]
    private static extern IntPtr GetForegroundWindow();

    [DllImport("user32.dll")]
    private static extern uint GetWindowThreadProcessId(IntPtr handle, out uint processId);

    [DllImport("user32.dll", CharSet = CharSet.Unicode, EntryPoint = "GetWindowTextW")]
    private static extern int GetWindowTextW(IntPtr handle, StringBuilder text, int maxCount);

    [DllImport("user32.dll", CharSet = CharSet.Unicode, EntryPoint = "GetWindowTextLengthW")]
    private static extern int GetWindowTextLengthW(IntPtr handle);

    [DllImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool GetLastInputInfo(ref LastInputInfo info);

    [StructLayout(LayoutKind.Sequential)]
    private struct LastInputInfo
    {
        public uint Size;
        public uint Time;
    }
}
