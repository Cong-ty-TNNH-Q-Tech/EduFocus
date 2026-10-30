using EduFocus.Ports.Outbound.Services;

namespace EduFocus.Service.Hosting;

/// <summary>
/// Bridges Service Control Manager power and session notifications to the
/// heartbeat. The handler runs on the SCM callback thread, so raising events
/// only flips flags; subscribers must not block.
/// </summary>
public sealed class ServicePowerNotifier : ISystemPowerEventPort
{
    public event EventHandler? Suspended;

    public event EventHandler? Resumed;

    public void RaiseSuspended() => Suspended?.Invoke(this, EventArgs.Empty);

    public void RaiseResumed() => Resumed?.Invoke(this, EventArgs.Empty);
}
