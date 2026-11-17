namespace EduFocus.Ports.Outbound.Cloud;

/// <summary>
/// Wakes the cloud sync worker as soon as a new outbox event is available so
/// the 60 second poll is only a fallback.
/// </summary>
public interface ICloudOutboxNotifier
{
    void NotifyNewEvent();
}
