namespace EduFocus.Ports.Outbound.Services;

/// <summary>
/// OS suspend/resume and session lock/unlock notifications (UC07, issue #28).
/// Handlers run on the OS event thread, so consumers must only record state and
/// do their work on their own loop.
/// </summary>
public interface ISystemPowerEventPort
{
    event EventHandler? Suspended;

    event EventHandler? Resumed;
}
