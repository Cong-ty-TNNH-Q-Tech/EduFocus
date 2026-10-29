using System.Globalization;

namespace EduFocus.Infrastructure.Watchdog;

public enum WatchdogCommandMode
{
    /// <summary>Normal WPF app.</summary>
    App,

    /// <summary>Resident watchdog loop (<c>--watchdog</c>).</summary>
    Watchdog,

    /// <summary>Signal the watchdog to stop (<c>--watchdog-stop</c>).</summary>
    Stop,

    /// <summary>Remove scheduled tasks, stop the watchdog and clear runtime files.</summary>
    Uninstall,

    /// <summary>Installer step: create the data directory with the restricted ACL.</summary>
    PrepareDataDirectory
}

/// <summary>
/// Parses the watchdog CLI before any WPF window, mutex or onboarding code runs.
/// Unknown arguments fall back to the normal app so a shortcut never blocks
/// startup.
/// </summary>
public sealed record WatchdogCommandLine(
    WatchdogCommandMode Mode,
    bool LoginStart,
    TimeSpan WaitTimeout,
    bool DeleteUserData = false)
{
    public const int DefaultWaitMilliseconds = 5000;
    public const int MaxWaitMilliseconds = 60000;

    public static WatchdogCommandLine Parse(IReadOnlyList<string>? args)
    {
        if (args is null || args.Count == 0)
            return new WatchdogCommandLine(WatchdogCommandMode.App, false, DefaultWait());

        var loginStart = args.Any(a => Equals(a, "--login"));
        var wait = ParseWait(args);

        if (args.Any(a => Equals(a, "--uninstall-watchdog")))
            return new WatchdogCommandLine(
                WatchdogCommandMode.Uninstall,
                false,
                wait,
                args.Any(a => Equals(a, "--delete-user-data")));

        if (args.Any(a => Equals(a, "--prepare-data-directory")))
            return new WatchdogCommandLine(WatchdogCommandMode.PrepareDataDirectory, false, wait);

        if (args.Any(a => Equals(a, "--watchdog-stop")))
            return new WatchdogCommandLine(WatchdogCommandMode.Stop, false, wait);

        if (args.Any(a => Equals(a, "--watchdog")))
            return new WatchdogCommandLine(WatchdogCommandMode.Watchdog, loginStart, wait);

        return new WatchdogCommandLine(WatchdogCommandMode.App, false, wait);
    }

    private static TimeSpan ParseWait(IReadOnlyList<string> args)
    {
        for (var index = 0; index < args.Count - 1; index++)
        {
            if (!Equals(args[index], "--wait-ms"))
                continue;

            if (!int.TryParse(
                    args[index + 1],
                    NumberStyles.Integer,
                    CultureInfo.InvariantCulture,
                    out var milliseconds))
            {
                break;
            }

            return TimeSpan.FromMilliseconds(Math.Clamp(milliseconds, 0, MaxWaitMilliseconds));
        }

        return DefaultWait();
    }

    private static TimeSpan DefaultWait() => TimeSpan.FromMilliseconds(DefaultWaitMilliseconds);

    private static bool Equals(string argument, string expected) =>
        string.Equals(argument, expected, StringComparison.OrdinalIgnoreCase);
}
