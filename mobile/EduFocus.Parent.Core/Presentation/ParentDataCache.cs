using System.Text.Json;
using EduFocus.Cloud.Contracts.Json;
using EduFocus.Cloud.Contracts.Notifications;
using EduFocus.Cloud.Contracts.Reports;
using EduFocus.Parent.Core.Settings;

namespace EduFocus.Parent.Core.Presentation;

public sealed record DailyCacheEntry(DateTimeOffset SavedAtUtc, List<DailySummaryDto> Days);

public sealed record NotificationsCacheEntry(
    DateTimeOffset SavedAtUtc,
    List<NotificationDto> Items,
    string? NextBefore);

/// <summary>
/// Small non-secret cache of aggregated data so the screens can open with the
/// last known values while offline. Never stores tokens or raw activity.
/// </summary>
public sealed class ParentDataCache(
    IKeyValueStore store,
    Func<DateTimeOffset>? clock = null,
    TimeSpan? maxAge = null)
{
    public const string DailyKey = "edufocus.parent.cache.daily";
    public const string NotificationsKey = "edufocus.parent.cache.notifications";

    private readonly TimeSpan _maxAge = maxAge ?? TimeSpan.FromDays(7);

    public void SaveDaily(IReadOnlyList<DailySummaryDto> days) =>
        Save(DailyKey, new DailyCacheEntry(Now(), days.ToList()));

    public void MergeDaily(IReadOnlyList<DailySummaryDto> days)
    {
        var merged = new Dictionary<DateOnly, DailySummaryDto>();
        var existing = Read<DailyCacheEntry>(DailyKey);
        if (existing is not null && IsFresh(existing.SavedAtUtc))
        {
            foreach (var day in existing.Days)
                merged[day.LocalDate] = day;
        }

        foreach (var day in days)
            merged[day.LocalDate] = day;

        var ordered = merged.Values
            .OrderByDescending(day => day.LocalDate)
            .Take(14)
            .ToList();

        Save(DailyKey, new DailyCacheEntry(Now(), ordered));
    }

    public bool TryGetDaily(out IReadOnlyList<DailySummaryDto> days, out DateTimeOffset savedAt)
    {
        days = [];
        savedAt = default;
        var entry = Read<DailyCacheEntry>(DailyKey);
        if (entry is null || !IsFresh(entry.SavedAtUtc))
            return false;

        days = entry.Days;
        savedAt = entry.SavedAtUtc;
        return true;
    }

    public void SaveNotifications(NotificationListResponse page) =>
        Save(NotificationsKey, new NotificationsCacheEntry(Now(), page.Items.ToList(), page.NextBefore));

    public bool TryGetNotifications(out NotificationListResponse page, out DateTimeOffset savedAt)
    {
        page = new NotificationListResponse([], null);
        savedAt = default;
        var entry = Read<NotificationsCacheEntry>(NotificationsKey);
        if (entry is null || !IsFresh(entry.SavedAtUtc))
            return false;

        page = new NotificationListResponse(entry.Items, entry.NextBefore);
        savedAt = entry.SavedAtUtc;
        return true;
    }

    public void Clear()
    {
        store.Remove(DailyKey);
        store.Remove(NotificationsKey);
    }

    private DateTimeOffset Now() => clock?.Invoke() ?? DateTimeOffset.UtcNow;

    private bool IsFresh(DateTimeOffset savedAt) => Now() - savedAt <= _maxAge;

    private void Save<T>(string key, T value)
    {
        try
        {
            store.Set(key, JsonSerializer.Serialize(value, ApiJson.Options));
        }
        catch (JsonException)
        {
            store.Remove(key);
        }
    }

    private T? Read<T>(string key) where T : class
    {
        var raw = store.Get(key);
        if (string.IsNullOrWhiteSpace(raw))
            return null;

        try
        {
            return JsonSerializer.Deserialize<T>(raw, ApiJson.Options);
        }
        catch (JsonException)
        {
            store.Remove(key);
            return null;
        }
    }
}
