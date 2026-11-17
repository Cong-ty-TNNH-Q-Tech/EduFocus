using EduFocus.Ports.Outbound.Cloud;

namespace EduFocus.UseCases.Tests.TestSupport;

internal sealed class RecordingCloudOutboxNotifier : ICloudOutboxNotifier
{
    public int SignalCount { get; private set; }

    public void NotifyNewEvent() => SignalCount++;
}
