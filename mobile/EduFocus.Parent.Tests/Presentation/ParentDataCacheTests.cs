using EduFocus.Cloud.Contracts.Notifications;
using EduFocus.Parent.Core.Presentation;
using EduFocus.Parent.Tests.TestSupport;

namespace EduFocus.Parent.Tests.Presentation;

public sealed class ParentDataCacheTests
{
    private static readonly DateOnly Today = new(2026, 11, 17);

    [Fact]
    public void Daily_summaries_round_trip_with_saved_time()
    {
        var now = new DateTimeOffset(2026, 11, 17, 3, 0, 0, TimeSpan.Zero);
        var keyValue = new FakeKeyValueStore();
        var cache = new ParentDataCache(keyValue, () => now);

        cache.SaveDaily([TestSummaries.Daily(Today, studySeconds: 8100)]);

        Assert.True(cache.TryGetDaily(out var days, out var savedAt));
        Assert.Equal(now, savedAt);
        var day = Assert.Single(days);
        Assert.Equal(Today, day.LocalDate);
        Assert.Equal(8100, day.StudySeconds);
        Assert.Equal(82.5, day.AttentionPercent);
    }

    [Fact]
    public void Notifications_round_trip_with_cursor()
    {
        var keyValue = new FakeKeyValueStore();
        var cache = new ParentDataCache(keyValue);
        var notification = TestNotifications.Notification();

        cache.SaveNotifications(new NotificationListResponse([notification], "cursor-1"));

        Assert.True(cache.TryGetNotifications(out var page, out _));
        var item = Assert.Single(page.Items);
        Assert.Equal(notification.Id, item.Id);
        Assert.Equal("cursor-1", page.NextBefore);
    }

    [Fact]
    public void Corrupted_cache_is_discarded()
    {
        var keyValue = new FakeKeyValueStore();
        keyValue.Set(ParentDataCache.DailyKey, "{not-json");
        var cache = new ParentDataCache(keyValue);

        Assert.False(cache.TryGetDaily(out _, out _));
        Assert.Null(keyValue.Get(ParentDataCache.DailyKey));
    }

    [Fact]
    public void Stale_cache_is_not_returned()
    {
        var now = new DateTimeOffset(2026, 11, 17, 3, 0, 0, TimeSpan.Zero);
        var keyValue = new FakeKeyValueStore();
        var cache = new ParentDataCache(keyValue, () => now);
        cache.SaveNotifications(new NotificationListResponse([TestNotifications.Notification()], null));

        now = now.AddDays(8);

        Assert.False(cache.TryGetNotifications(out _, out _));
    }

    [Fact]
    public void Clear_removes_both_entries()
    {
        var keyValue = new FakeKeyValueStore();
        var cache = new ParentDataCache(keyValue);
        cache.SaveDaily([TestSummaries.Daily(Today)]);
        cache.SaveNotifications(new NotificationListResponse([], null));

        cache.Clear();

        Assert.False(cache.TryGetDaily(out _, out _));
        Assert.False(cache.TryGetNotifications(out _, out _));
    }
}
