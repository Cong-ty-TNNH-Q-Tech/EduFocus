using EduFocus.Parent.Core.Push;

namespace EduFocus.Parent.Tests.Push;

public sealed class PushNotificationIdTests
{
    [Fact]
    public void Id_is_stable_for_the_same_key()
    {
        var first = PushNotificationId.FromKey("daily_summary:0f4a8f0e");
        var second = PushNotificationId.FromKey("daily_summary:0f4a8f0e");

        Assert.Equal(first, second);
        Assert.True(first >= 0);
    }

    [Fact]
    public void Different_keys_use_different_ids()
    {
        var ids = Enumerable.Range(0, 32)
            .Select(index => PushNotificationId.FromKey($"attention_alert:{index}"))
            .ToArray();

        Assert.Equal(ids.Length, ids.Distinct().Count());
    }
}
