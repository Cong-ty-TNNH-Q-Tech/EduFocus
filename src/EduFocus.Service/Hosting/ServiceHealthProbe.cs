using System.Runtime.InteropServices;
using System.Runtime.Versioning;
using EduFocus.Ports.ServiceControl;

namespace EduFocus.Service.Hosting;

/// <summary>
/// <c>--health</c> reads the service state from the SCM instead of the pipe so
/// the diagnostic works even while the control endpoint is starting, failing
/// or stopped. It never carries data and needs only SERVICE_QUERY_STATUS.
/// </summary>
[SupportedOSPlatform("windows")]
public static class ServiceHealthProbe
{
    private const uint ScManagerConnect = 0x0001;
    private const uint ServiceQueryStatus = 0x0004;

    private const uint ServiceStopped = 0x00000001;
    private const uint ServiceStartPending = 0x00000002;
    private const uint ServiceStopPending = 0x00000003;
    private const uint ServiceRunning = 0x00000004;
    private const uint ServiceContinuePending = 0x00000005;
    private const uint ServicePausePending = 0x00000006;
    private const uint ServicePaused = 0x00000007;

    public static int Run()
    {
        IntPtr manager = IntPtr.Zero;
        IntPtr service = IntPtr.Zero;

        try
        {
            manager = OpenSCManagerW(null, null, ScManagerConnect);
            if (manager == IntPtr.Zero)
            {
                Console.Error.WriteLine(
                    $"Could not open the service manager (Win32 {Marshal.GetLastWin32Error()}). Run --health from an elevated prompt.");
                return 2;
            }

            service = OpenServiceW(manager, ServiceIdentity.WindowsServiceName, ServiceQueryStatus);
            if (service == IntPtr.Zero)
            {
                var error = Marshal.GetLastWin32Error();
                Console.WriteLine(
                    error == 1060
                        ? $"{ServiceIdentity.WindowsServiceName}: not installed."
                        : $"{ServiceIdentity.WindowsServiceName}: unavailable (Win32 {error}).");
                return 1;
            }

            if (!QueryServiceStatus(service, out var status))
            {
                Console.Error.WriteLine(
                    $"Could not query {ServiceIdentity.WindowsServiceName} (Win32 {Marshal.GetLastWin32Error()}).");
                return 2;
            }

            Console.WriteLine($"{ServiceIdentity.WindowsServiceName}: {Describe(status.CurrentState)}.");
            return status.CurrentState == ServiceRunning ? 0 : 1;
        }
        finally
        {
            if (service != IntPtr.Zero)
                CloseServiceHandle(service);
            if (manager != IntPtr.Zero)
                CloseServiceHandle(manager);
        }
    }

    private static string Describe(uint state) => state switch
    {
        ServiceStopped => "stopped",
        ServiceStartPending => "start pending",
        ServiceStopPending => "stop pending",
        ServiceRunning => "running",
        ServiceContinuePending => "continue pending",
        ServicePausePending => "pause pending",
        ServicePaused => "paused",
        _ => $"unknown ({state})"
    };

    [StructLayout(LayoutKind.Sequential)]
    private struct ServiceStatus
    {
        public uint ServiceType;
        public uint CurrentState;
        public uint ControlsAccepted;
        public uint Win32ExitCode;
        public uint ServiceSpecificExitCode;
        public uint CheckPoint;
        public uint WaitHint;
    }

    [DllImport("advapi32.dll", EntryPoint = "OpenSCManagerW", SetLastError = true, CharSet = CharSet.Unicode)]
    private static extern IntPtr OpenSCManagerW(string? machineName, string? databaseName, uint desiredAccess);

    [DllImport("advapi32.dll", EntryPoint = "OpenServiceW", SetLastError = true, CharSet = CharSet.Unicode)]
    private static extern IntPtr OpenServiceW(IntPtr manager, string serviceName, uint desiredAccess);

    [DllImport("advapi32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool QueryServiceStatus(IntPtr service, out ServiceStatus status);

    [DllImport("advapi32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool CloseServiceHandle(IntPtr handle);
}
