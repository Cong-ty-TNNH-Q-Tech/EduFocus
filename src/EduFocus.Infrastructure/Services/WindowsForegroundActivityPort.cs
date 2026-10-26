using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Text;
using EduFocus.Domain.Enums;
using EduFocus.Ports.Outbound.Services;

namespace EduFocus.Infrastructure.Services;

/// <summary>Captures foreground process metadata without reading page contents, clipboard, or keyboard input.</summary>
public sealed class WindowsForegroundActivityPort : IForegroundActivityPort
{
    public ForegroundActivitySnapshot Capture()
    {
        var idleSeconds = GetIdleSeconds();
        var window = GetForegroundWindow();
        if (window == IntPtr.Zero) return new(null, null, null, null, ActivityUrlSource.None, idleSeconds);

        _ = GetWindowThreadProcessId(window, out var processId);
        try
        {
            using var process = Process.GetProcessById(unchecked((int)processId));
            var processName = SafeText(process.ProcessName, 128);
            var title = new StringBuilder(1024);
            var length = GetWindowText(window, title, title.Capacity);
            var windowTitle = length <= 0 ? null : SafeText(title.ToString(), 512);
            return new(processName, processName, windowTitle, null, ActivityUrlSource.None, idleSeconds);
        }
        catch (ArgumentException)
        {
            // A foreground app can exit between reading its process ID and opening it.
            return new(null, null, null, null, ActivityUrlSource.None, idleSeconds);
        }
        catch (InvalidOperationException)
        {
            return new(null, null, null, null, ActivityUrlSource.None, idleSeconds);
        }
    }

    private static long GetIdleSeconds()
    {
        var input = new LastInputInfo { Size = (uint)Marshal.SizeOf<LastInputInfo>() };
        if (!GetLastInputInfo(ref input)) return 0;
        var elapsed = unchecked((uint)Environment.TickCount - input.Time);
        return elapsed / 1000;
    }

    private static string? SafeText(string value, int maxLength)
    {
        var filtered = new string(value.Where(c => !char.IsControl(c)).ToArray()).Trim();
        return filtered.Length == 0 ? null : filtered[..Math.Min(filtered.Length, maxLength)];
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct LastInputInfo { public uint Size; public uint Time; }

    [DllImport("user32.dll")] private static extern IntPtr GetForegroundWindow();
    [DllImport("user32.dll", CharSet = CharSet.Unicode)] private static extern int GetWindowText(IntPtr window, StringBuilder text, int maxCount);
    [DllImport("user32.dll")] private static extern uint GetWindowThreadProcessId(IntPtr window, out uint processId);
    [DllImport("user32.dll")] private static extern bool GetLastInputInfo(ref LastInputInfo info);
}
