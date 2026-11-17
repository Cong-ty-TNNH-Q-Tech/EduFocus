namespace EduFocus.Parent.Core.Push;

/// <summary>
/// Stable 32-bit id for Android notifications and PendingIntents. Unlike
/// <see cref="string.GetHashCode()"/> it does not change between processes, so
/// a redelivered message reuses the same notification slot.
/// </summary>
public static class PushNotificationId
{
    public static int FromKey(string key)
    {
        unchecked
        {
            var hash = 2166136261u;
            foreach (var character in key)
            {
                hash ^= character;
                hash *= 16777619u;
            }

            return (int)(hash & 0x7FFFFFFF);
        }
    }
}
