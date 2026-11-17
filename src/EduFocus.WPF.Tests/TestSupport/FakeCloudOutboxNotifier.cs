using EduFocus.Ports.Outbound.Cloud;

namespace EduFocus.WPF.Tests.TestSupport;

internal sealed class FakeCloudOutboxNotifier : ICloudOutboxNotifier
{
    public int NotifyCalls { get; private set; }

    public void NotifyNewEvent() => NotifyCalls++;
}
