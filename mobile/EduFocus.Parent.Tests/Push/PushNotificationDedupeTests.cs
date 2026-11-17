using EduFocus.Parent.Core.Push;
using EduFocus.Parent.Tests.TestSupport;

namespace EduFocus.Parent.Tests.Push;

public sealed class PushNotificationDedupeTests
{
    private DateTimeOffset _now = new(2026, 11, 17, 10, 0, 0, TimeSpan.Zero);

    private PushNotificationDedupe Create(FakeKeyValueStore? store = null) =>
        new(store ?? new FakeKeyValueStore(), () => _now);

    [Fact]
    public void First_delivery_is_shown_and_repeat_is_suppressed()
    {
        var dedupe = Create();

        Assert.True(dedupe.TryRemember("daily_summary:id-1"));
        Assert.False(dedupe.TryRemember("daily_summary:id-1"));
        Assert.True(dedupe.TryRemember("daily_summary:id-2"));
    }

    [Fact]
    public void Repeat_after_ttl_is_shown_again()
    {
        var dedupe = Create();

        Assert.True(dedupe.TryRemember("security_alert:id-1"));
        _now = _now.AddDays(8);

        Assert.True(dedupe.TryRemember("security_alert:id-1"));
    }

    [Fact]
    public void Persists_across_instances()
    {
        var store = new FakeKeyValueStore();
        Assert.True(Create(store).TryRemember("daily_summary:id-1"));

        Assert.False(Create(store).TryRemember("daily_summary:id-1"));
    }

    [Fact]
    public void Oldest_entries_are_evicted_beyond_capacity()
    {
        var store = new FakeKeyValueStore();
        var dedupe = Create(store);

        for (var index = 0; index < 70; index++)
            Assert.True(dedupe.TryRemember($"attention_alert:id-{index}"));

        Assert.False(dedupe.TryRemember("attention_alert:id-69"));
        Assert.True(dedupe.TryRemember("attention_alert:id-0"));
    }

    [Fact]
    public void Corrupted_storage_is_ignored()
    {
        var store = new FakeKeyValueStore();
        store.Set(PushNotificationDedupe.StorageKey, "garbage\nnonsense|not-a-number\n\n|42");
        var dedupe = Create(store);

        Assert.True(dedupe.TryRemember("daily_summary:id-1"));
    }

    [Fact]
    public void Clear_removes_all_entries()
    {
        var dedupe = Create();
        Assert.True(dedupe.TryRemember("daily_summary:id-1"));

        dedupe.Clear();

        Assert.True(dedupe.TryRemember("daily_summary:id-1"));
    }
}
