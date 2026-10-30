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

            var buffer = new StringBuilder(1024);
            var size = buffer.Capacity;
            if (!QueryFullProcessImageNameW(handle, 0, buffer, ref size))
                return null;

            return buffer.ToString();
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
                Path.GetFullPath(left).TrimEnd(Path.DirectorySeparatorChar),
                Path.GetFullPath(right).TrimEnd(Path.DirectorySeparatorChar),
                StringComparison.OrdinalIgnoreCase);
        }
        catch (Exception ex) when (ex is ArgumentException or NotSupportedException or PathTooLongException)
        {
            return false;
        }
    }

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
