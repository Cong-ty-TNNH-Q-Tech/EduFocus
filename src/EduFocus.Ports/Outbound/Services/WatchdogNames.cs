namespace EduFocus.Ports.Outbound.Services;

/// <summary>
/// Named objects shared by the app and the watchdog. The watchdog mutex and stop
/// event are global so only one supervisor exists per machine and an elevated
/// uninstaller can always signal it.
/// </summary>
public static class WatchdogNames
{
    public const string AppInstanceMutex = "EduFocus_SingleInstance_Mutex";
    public const string WatchdogMutex = @"Global\EduFocus_Watchdog_Mutex";
    public const string WatchdogStopEvent = @"Global\EduFocus_Watchdog_Stop";
    public const string LocalWatchdogMutex = @"Local\EduFocus_Watchdog_Mutex";
    public const string LocalWatchdogStopEvent = @"Local\EduFocus_Watchdog_Stop";
    public const string SessionEndingEvent = "EduFocus_SessionEnding";
    public const string LogonTaskName = "EduFocusLogon";
    public const string HealthTaskName = "EduFocusWatchdog";

    /// <summary>
    /// The global namespace is only usable by elevated processes; a Debug app
    /// and its watchdog fall back to the session-local namespace together.
    /// </summary>
    public static string WatchdogMutexForCurrentProcess =>
        Environment.IsPrivilegedProcess ? WatchdogMutex : LocalWatchdogMutex;

    public static string WatchdogStopEventForCurrentProcess =>
        Environment.IsPrivilegedProcess ? WatchdogStopEvent : LocalWatchdogStopEvent;
}
