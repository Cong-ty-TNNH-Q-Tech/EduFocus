using System.Runtime.Versioning;
using EduFocus.Ports.Outbound.Services;
using Microsoft.Win32;

namespace EduFocus.WPF.Services;

/// <summary>
/// Bridges Windows power and session notifications to
/// <see cref="ISystemPowerEventPort"/>. Sleep, hibernate, session lock and
/// console disconnect all pause time accounting; resume/unlock re-anchors the
/// monotonic clocks without counting the away gap.
/// </summary>
[SupportedOSPlatform("windows")]
public sealed class WindowsSystemPowerEventPort : ISystemPowerEventPort, IDisposable
{
    private bool _disposed;

    public WindowsSystemPowerEventPort()
    {
        SystemEvents.PowerModeChanged += OnPowerModeChanged;
        SystemEvents.SessionSwitch += OnSessionSwitch;
    }

    public event EventHandler? Suspended;

    public event EventHandler? Resumed;

    public void Dispose()
    {
        if (_disposed)
            return;

        _disposed = true;
        SystemEvents.PowerModeChanged -= OnPowerModeChanged;
        SystemEvents.SessionSwitch -= OnSessionSwitch;
    }

    private void OnPowerModeChanged(object sender, PowerModeChangedEventArgs e)
    {
        switch (e.Mode)
        {
            case PowerModes.Suspend:
                Suspended?.Invoke(this, EventArgs.Empty);
                break;
            case PowerModes.Resume:
                Resumed?.Invoke(this, EventArgs.Empty);
                break;
        }
    }

    private void OnSessionSwitch(object sender, SessionSwitchEventArgs e)
    {
        switch (e.Reason)
        {
            case SessionSwitchReason.SessionLock:
            case SessionSwitchReason.ConsoleDisconnect:
            case SessionSwitchReason.RemoteDisconnect:
                Suspended?.Invoke(this, EventArgs.Empty);
                break;
            case SessionSwitchReason.SessionUnlock:
            case SessionSwitchReason.ConsoleConnect:
            case SessionSwitchReason.RemoteConnect:
                Resumed?.Invoke(this, EventArgs.Empty);
                break;
        }
    }
}
