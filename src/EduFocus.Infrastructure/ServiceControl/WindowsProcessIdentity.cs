using System.ComponentModel;
using System.IO.Pipes;
using System.Runtime.InteropServices;
using System.Runtime.Versioning;
using System.Text;
using Microsoft.Win32.SafeHandles;

namespace EduFocus.Infrastructure.ServiceControl;

/// <summary>
/// Minimal Win32 helpers for named-pipe peer identification. .NET does not
/// expose the client/server process id on the pipe stream, so both sides use
/// kernel32 directly and then read the image path with limited query rights.
/// </summary>
[SupportedOSPlatform("windows")]
internal static class WindowsProcessIdentity
{
    private const uint ProcessQueryLimitedInformation = 0x1000;

    public static int? GetClientProcessId(NamedPipeServerStream pipe)
    {
        try
        {
            return GetNamedPipeClientProcessId(pipe.SafePipeHandle, out var processId)
                ? checked((int)processId)
                : null;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            return null;
        }
    }

    public static int? GetServerProcessId(NamedPipeClientStream pipe)
    {
        try
        {
            return GetNamedPipeServerProcessId(pipe.SafePipeHandle, out var processId)
                ? checked((int)processId)
                : null;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            return null;
        }
    }

    public static string? TryGetImagePath(int processId)
    {
        SafeProcessHandle? handle = null;

        try
        {
            handle = OpenProcess(ProcessQueryLimitedInformation, false, (uint)processId);
            if (handle.IsInvalid)
                return null;

            const int ErrorInsufficientBuffer = 122;
            var capacity = 1024;
            while (capacity <= 32768)
            {
                var buffer = new StringBuilder(capacity);
                var size = buffer.Capacity;
                if (QueryFullProcessImageNameW(handle, 0, buffer, ref size))
                    return buffer.ToString();

                if (Marshal.GetLastWin32Error() != ErrorInsufficientBuffer)
                    return null;

                capacity *= 2;
            }

            return null;
        }
        catch (Exception ex) when (ex is Win32Exception or UnauthorizedAccessException or IOException)
        {
            return null;
        }
        finally
        {
            handle?.Dispose();
        }
    }

    public static bool PathsEqual(string left, string right)
    {
        try
        {
            return string.Equals(
                NormalizePath(left),
                NormalizePath(right),
                StringComparison.OrdinalIgnoreCase);
        }
        catch (Exception ex) when (ex is ArgumentException or NotSupportedException or PathTooLongException)
        {
            return false;
        }
    }

    private static string NormalizePath(string path)
    {
        var full = Path.GetFullPath(path).TrimEnd(Path.DirectorySeparatorChar);
        var length = GetLongPathNameW(full, null, 0);
        if (length == 0)
            return full;

        var buffer = new StringBuilder(checked((int)length + 1));
        var written = GetLongPathNameW(full, buffer, (uint)buffer.Capacity);
        if (written > 0 && written < buffer.Capacity)
            return Path.GetFullPath(buffer.ToString()).TrimEnd(Path.DirectorySeparatorChar);

        return full;
    }

    [DllImport("kernel32.dll", SetLastError = true, CharSet = CharSet.Unicode)]
    private static extern uint GetLongPathNameW(
        string shortPath,
        StringBuilder? longPath,
        uint bufferLength);

    [DllImport("kernel32.dll", SetLastError = true)]
    private static extern bool GetNamedPipeClientProcessId(
        SafePipeHandle pipe,
        out uint clientProcessId);

    [DllImport("kernel32.dll", SetLastError = true)]
    private static extern bool GetNamedPipeServerProcessId(
        SafePipeHandle pipe,
        out uint serverProcessId);

    [DllImport("kernel32.dll", SetLastError = true)]
    private static extern SafeProcessHandle OpenProcess(
        uint desiredAccess,
        bool inheritHandle,
        uint processId);

    [DllImport("kernel32.dll", SetLastError = true, CharSet = CharSet.Unicode)]
    private static extern bool QueryFullProcessImageNameW(
        SafeProcessHandle process,
        int flags,
        StringBuilder imagePath,
        ref int size);
}
