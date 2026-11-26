using System.Diagnostics;

namespace EduFocus.Parent.Core.Push;

/// <summary>
/// Broadcasts validated incoming push notifications to active Mobile UI pages
/// so visible views refresh immediately without waiting for manual navigation.
/// </summary>
public sealed class PushEventHub
{
    public event Action<PushNotificationPayload>? Received;

    public void Publish(PushNotificationPayload payload)
    {
        ArgumentNullException.ThrowIfNull(payload);
        var handlers = Received;
        if (handlers is null)
            return;

        foreach (Action<PushNotificationPayload> handler in handlers.GetInvocationList())
        {
            try
            {
                handler(payload);
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"Push UI subscriber failed: {ex.GetType().Name}");
            }
        }
    }
}
