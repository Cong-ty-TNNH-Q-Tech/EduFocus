using EduFocus.Cloud.Contracts.Notifications;
using EduFocus.Cloud.Contracts.Preferences;
using EduFocus.Cloud.Contracts.Reports;
using EduFocus.Parent.Core.Presentation;

namespace EduFocus.Parent.Tests.TestSupport;

internal sealed class FakeParentDataService : IParentDataService
{
    public List<(DateOnly From, DateOnly To)> DailyRequests { get; } = [];

    public List<(int Limit, string? Before)> NotificationRequests { get; } = [];

    public List<UpdateNotificationPreferencesRequest> PreferenceUpdates { get; } = [];

    public Exception? DailyError { get; set; }

    public Exception? NotificationsError { get; set; }

    public Exception? PreferencesError { get; set; }

    public Exception? UpdatePreferencesError { get; set; }

    public IReadOnlyList<DailySummaryDto> DailyResult { get; set; } = [];

    public List<NotificationListResponse> NotificationPages { get; } = [];

    public NotificationPreferencesDto PreferencesResult { get; set; } = DefaultPreferences();

    public NotificationPreferencesDto? UpdatePreferencesResult { get; set; }

    public Task<IReadOnlyList<DailySummaryDto>> GetDailyAsync(
        DateOnly from,
        DateOnly to,
        CancellationToken cancellationToken = default)
    {
        DailyRequests.Add((from, to));
        return DailyError is null
            ? Task.FromResult(DailyResult)
            : Task.FromException<IReadOnlyList<DailySummaryDto>>(DailyError);
    }

    public Task<NotificationListResponse> GetNotificationsAsync(
        int limit,
        string? before,
        CancellationToken cancellationToken = default)
    {
        NotificationRequests.Add((limit, before));
        if (NotificationsError is not null)
            return Task.FromException<NotificationListResponse>(NotificationsError);

        var page = NotificationPages.Count == 0
            ? new NotificationListResponse([], null)
            : NotificationPages[Math.Min(NotificationRequests.Count - 1, NotificationPages.Count - 1)];
        return Task.FromResult(page);
    }

    public Task<NotificationPreferencesDto> GetPreferencesAsync(
        CancellationToken cancellationToken = default) =>
        PreferencesError is null
            ? Task.FromResult(PreferencesResult)
            : Task.FromException<NotificationPreferencesDto>(PreferencesError);

    public Task<NotificationPreferencesDto> UpdatePreferencesAsync(
        UpdateNotificationPreferencesRequest request,
        CancellationToken cancellationToken = default)
    {
        PreferenceUpdates.Add(request);
        if (UpdatePreferencesError is not null)
            return Task.FromException<NotificationPreferencesDto>(UpdatePreferencesError);

        return Task.FromResult(UpdatePreferencesResult ?? new NotificationPreferencesDto(
            request.AttentionAlert,
            request.DailySummary,
            request.ProtectionOff,
            request.SecurityAlert,
            request.QuietStartMinutes,
            request.QuietEndMinutes,
            request.DailySummaryMinutes,
            request.TimeZoneId,
            request.SecurityAlertBypassQuietHours));
    }

    public static NotificationPreferencesDto DefaultPreferences() =>
        new(true, true, true, true, 22 * 60, 6 * 60, 20 * 60 + 30, "Asia/Ho_Chi_Minh", true);
}

internal static class TestSummaries
{
    public static readonly DateTimeOffset GeneratedAt =
        new(2026, 11, 17, 10, 30, 0, TimeSpan.Zero);

    public static DailySummaryDto Daily(
        DateOnly date,
        long studySeconds = 8100,
        long activeSeconds = 9000,
        long onlineClassSeconds = 1800,
        int sessions = 3,
        int blocked = 2,
        double? attention = 82.5,
        double? coverage = 96) =>
        new(
            date,
            "Asia/Ho_Chi_Minh",
            GeneratedAt,
            sessions,
            activeSeconds,
            studySeconds,
            600,
            onlineClassSeconds,
            7200,
            6900,
            5700,
            900,
            300,
            100,
            blocked,
            attention,
            coverage);
}

internal static class TestNotifications
{
    public static NotificationDto Notification(
        NotificationKind kind = NotificationKind.DailySummary,
        NotificationDeliveryStatus status = NotificationDeliveryStatus.Sent,
        DateTimeOffset? createdAt = null) =>
        new(
            Guid.NewGuid(),
            kind,
            status,
            createdAt ?? new DateTimeOffset(2026, 11, 17, 10, 30, 0, TimeSpan.Zero),
            null);
}
