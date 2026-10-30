using EduFocus.Ports.Outbound.Services;

namespace EduFocus.UseCases.Tests.TestSupport;

internal sealed class FakeSystemPowerEventPort : ISystemPowerEventPort
{
    public event EventHandler? Suspended;

    public event EventHandler? Resumed;

    public void RaiseSuspended() => Suspended?.Invoke(this, EventArgs.Empty);

    public void RaiseResumed() => Resumed?.Invoke(this, EventArgs.Empty);
}
