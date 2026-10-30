using System.Runtime.InteropServices;
using System.Runtime.Versioning;
using EduFocus.Ports.Outbound.Services;
using EduFocus.Ports.ServiceControl;
using EduFocus.Service.Runtime;
using Microsoft.Extensions.DependencyInjection;

namespace EduFocus.Service.Hosting;

/// <summary>
/// Minimal Service Control Manager host. .NET 10 no longer ships
/// System.ServiceProcess, so this uses the four Win32 entry points a service
/// needs: dispatcher, handler, status and the SCM tables. It reports START
/// PENDING until the runtime has reconciled protection, RUNNING afterwards and
/// STOP PENDING while loops and in-flight commands drain.
/// </summary>
[SupportedOSPlatform("windows")]
public sealed class WindowsServiceHost
{
    private const uint ServiceWin32OwnProcess = 0x00000010;

    private const uint ServiceAcceptStop = 0x00000001;
    private const uint ServiceAcceptShutdown = 0x00000004;
    private const uint ServiceAcceptPowerEvent = 0x00000040;
    private const uint ServiceAcceptSessionChange = 0x00000080;

    private const uint ServiceControlStop = 0x00000001;
    private const uint ServiceControlInterrogate = 0x00000004;
    private const uint ServiceControlShutdown = 0x00000005;
    private const uint ServiceControlPowerEvent = 0x0000000D;
    private const uint ServiceControlSessionChange = 0x0000000E;

    private const uint ServiceStopped = 0x00000001;
    private const uint ServiceStartPending = 0x00000002;
    private const uint ServiceStopPending = 0x00000003;
    private const uint ServiceRunning = 0x00000004;

    private const uint NoError = 0;
    private const uint ErrorServiceSpecificError = 1066;

    private const uint PbtApmSuspend = 0x0004;
    private const uint PbtApmResumeSuspend = 0x0007;
    private const uint PbtApmResumeAutomatic = 0x0012;

    private const uint WtsConsoleConnect = 0x00000001;
    private const uint WtsConsoleDisconnect = 0x00000002;
    private const uint WtsRemoteConnect = 0x00000003;
    private const uint WtsRemoteDisconnect = 0x00000004;
    private const uint WtsSessionLock = 0x00000007;
    private const uint WtsSessionUnlock = 0x00000008;

    private const uint WaitHintMs = 30_000;

    private static readonly TimeSpan StopTimeout = TimeSpan.FromSeconds(25);

    private readonly ServiceProvider _provider;
    private readonly ServiceRuntime _runtime;
    private readonly ServicePowerNotifier _power;
    private readonly IAppLogger _logger;
    private readonly ServiceMainDelegate _serviceMain;
    private readonly HandlerExDelegate _handlerEx;
    private readonly object _statusLock = new();
    private readonly CancellationTokenSource _stop = new();

    private IntPtr _statusHandle;
    private int _stopRequested;
    private int _stopStarted;
    private int _providerDisposed;
    private int _exitCode;

    public WindowsServiceHost(
        ServiceProvider provider,
        ServicePowerNotifier powerNotifier)
    {
        _provider = provider ?? throw new ArgumentNullException(nameof(provider));
        _power = powerNotifier ?? throw new ArgumentNullException(nameof(powerNotifier));
        _runtime = provider.GetRequiredService<ServiceRuntime>();
        _logger = provider.GetRequiredService<IAppLogger>();
        _serviceMain = ServiceMain;
        _handlerEx = HandleControl;
    }

    public int Run()
    {
        var serviceNamePointer = Marshal.StringToHGlobalUni(ServiceIdentity.WindowsServiceName);

        try
        {
            var table = new[]
            {
                new ServiceTableEntry
                {
                    ServiceName = serviceNamePointer,
                    ServiceMain = Marshal.GetFunctionPointerForDelegate(_serviceMain)
                },
                default
            };

            if (!StartServiceCtrlDispatcherW(table))
            {
                var error = Marshal.GetLastWin32Error();
                Console.Error.WriteLine(
                    $"EduFocus.Service must be started by the Windows Service Control Manager (Win32 {error}). " +
                    "Run with --console for a diagnostic host.");
                return 1;
            }

            return Volatile.Read(ref _exitCode);
        }
        finally
        {
            Marshal.FreeHGlobal(serviceNamePointer);
            DisposeProviderOnceAsync().GetAwaiter().GetResult();
        }
    }

    private void ServiceMain(int argumentCount, IntPtr arguments)
    {
        _statusHandle = RegisterServiceCtrlHandlerExW(
            ServiceIdentity.WindowsServiceName,
            _handlerEx,
            IntPtr.Zero);

        if (_statusHandle == IntPtr.Zero)
        {
            _logger.Error(
                LogCategories.App,
                $"Could not register the service control handler (Win32 {Marshal.GetLastWin32Error()}).");
            Volatile.Write(ref _exitCode, 1);
            return;
        }

        ReportStatus(ServiceStartPending, controlsAccepted: 0, checkpoint: 1);
        ReportStatus(ServiceStartPending, controlsAccepted: 0, checkpoint: 2);

        _ = Task.Run(StartRuntimeAsync);
    }

    private async Task StartRuntimeAsync()
    {
        try
        {
            await _runtime.StartAsync(_stop.Token).ConfigureAwait(false);

            if (Volatile.Read(ref _stopRequested) != 0)
            {
                await StopRuntimeAsync().ConfigureAwait(false);
                return;
            }

            ReportStatus(
                ServiceRunning,
                ServiceAcceptStop | ServiceAcceptShutdown | ServiceAcceptPowerEvent | ServiceAcceptSessionChange,
                checkpoint: 0);
        }
        catch (Exception ex)
        {
            _logger.Error(LogCategories.App, "The protection service runtime failed to start.", ex);
            Volatile.Write(ref _exitCode, 1);
            ReportStatus(
                ServiceStopped,
                controlsAccepted: 0,
                checkpoint: 0,
                win32ExitCode: ErrorServiceSpecificError,
                serviceSpecificExitCode: 1);
        }
    }

    private int HandleControl(uint control, uint eventType, IntPtr eventData, IntPtr context)
    {
        try
        {
            switch (control)
            {
                case ServiceControlStop:
                    RequestStop();
                    return 0;

                case ServiceControlShutdown:
                    RequestStop();
                    return 0;

                case ServiceControlInterrogate:
                    return 0;

                case ServiceControlPowerEvent:
                    HandlePowerEvent(eventType);
                    return 0;

                case ServiceControlSessionChange:
                    HandleSessionChange(eventType);
                    return 0;

                default:
                    return 120; // ERROR_CALL_NOT_IMPLEMENTED
            }
        }
        catch (Exception ex)
        {
            _logger.Error(LogCategories.App, "The service control handler failed.", ex);
            return 0;
        }
    }

    private void RequestStop()
    {
        if (Interlocked.Exchange(ref _stopRequested, 1) != 0)
            return;

        _ = Task.Run(StopRuntimeAsync);
    }

    private async Task StopRuntimeAsync()
    {
        if (Interlocked.Exchange(ref _stopStarted, 1) != 0)
            return;

        ReportStatus(ServiceStopPending, controlsAccepted: 0, checkpoint: 1);

        var exitCode = 0;

        try
        {
            using var timeout = new CancellationTokenSource(StopTimeout);
            await _runtime.StopAsync(timeout.Token).ConfigureAwait(false);
        }
        catch (OperationCanceledException)
        {
            _logger.Warning(LogCategories.App, "The protection service runtime did not stop in time.");
            exitCode = 1;
        }
        catch (Exception ex)
        {
            _logger.Error(LogCategories.App, "The protection service runtime failed to stop cleanly.", ex);
            exitCode = 1;
        }

        if (!await DisposeProviderOnceAsync().ConfigureAwait(false))
            exitCode = 1;

        Volatile.Write(ref _exitCode, exitCode);
        _stop.Dispose();

        ReportStatus(ServiceStopped, controlsAccepted: 0, checkpoint: 0);
    }

    private async Task<bool> DisposeProviderOnceAsync()
    {
        if (Interlocked.Exchange(ref _providerDisposed, 1) != 0)
            return true;

        try
        {
            await _provider.DisposeAsync().ConfigureAwait(false);
            return true;
        }
        catch (Exception ex)
        {
            _logger.Error(LogCategories.App, "Disposing the protection service provider failed.", ex);
            return false;
        }
    }

    private void HandlePowerEvent(uint eventType)
    {
        switch (eventType)
        {
            case PbtApmSuspend:
                _power.RaiseSuspended();
                break;

            case PbtApmResumeSuspend:
            case PbtApmResumeAutomatic:
                _power.RaiseResumed();
                break;
        }
    }

    private void HandleSessionChange(uint eventType)
    {
        switch (eventType)
        {
            case WtsSessionLock:
            case WtsConsoleDisconnect:
            case WtsRemoteDisconnect:
                _power.RaiseSuspended();
                break;

            case WtsSessionUnlock:
            case WtsConsoleConnect:
            case WtsRemoteConnect:
                _power.RaiseResumed();
                break;
        }
    }

    private void ReportStatus(
        uint currentState,
        uint controlsAccepted,
        uint checkpoint,
        uint win32ExitCode = NoError,
        uint serviceSpecificExitCode = 0)
    {
        lock (_statusLock)
        {
            if (_statusHandle == IntPtr.Zero)
                return;

            var status = new ServiceStatus
            {
                ServiceType = ServiceWin32OwnProcess,
                CurrentState = currentState,
                ControlsAccepted = controlsAccepted,
                Win32ExitCode = win32ExitCode,
                ServiceSpecificExitCode = serviceSpecificExitCode,
                CheckPoint = checkpoint,
                WaitHint = currentState is ServiceStartPending or ServiceStopPending ? WaitHintMs : 0
            };

            if (!SetServiceStatus(_statusHandle, ref status))
            {
                _logger.Warning(
                    LogCategories.App,
                    $"Could not report service status {currentState} (Win32 {Marshal.GetLastWin32Error()}).");
            }
        }
    }

    private delegate void ServiceMainDelegate(int argumentCount, IntPtr arguments);

    private delegate int HandlerExDelegate(uint control, uint eventType, IntPtr eventData, IntPtr context);

    [StructLayout(LayoutKind.Sequential)]
    private struct ServiceTableEntry
    {
        public IntPtr ServiceName;
        public IntPtr ServiceMain;
    }

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

    [DllImport("advapi32.dll", EntryPoint = "StartServiceCtrlDispatcherW", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool StartServiceCtrlDispatcherW(ServiceTableEntry[] serviceTable);

    [DllImport("advapi32.dll", EntryPoint = "RegisterServiceCtrlHandlerExW", SetLastError = true, CharSet = CharSet.Unicode)]
    private static extern IntPtr RegisterServiceCtrlHandlerExW(
        string serviceName,
        HandlerExDelegate handler,
        IntPtr context);

    [DllImport("advapi32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool SetServiceStatus(IntPtr statusHandle, ref ServiceStatus serviceStatus);
}
