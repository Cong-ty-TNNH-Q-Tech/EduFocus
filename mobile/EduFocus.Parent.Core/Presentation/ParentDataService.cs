using System.Globalization;
using EduFocus.Cloud.Contracts.Notifications;
using EduFocus.Cloud.Contracts.Preferences;
using EduFocus.Cloud.Contracts.Reports;
using EduFocus.Parent.Core.Services;

namespace EduFocus.Parent.Core.Presentation;

public interface IParentDataService
{
    Task<IReadOnlyList<DailySummaryDto>> GetDailyAsync(
        DateOnly from,
        DateOnly to,
        CancellationToken cancellationToken = default);

    Task<NotificationListResponse> GetNotificationsAsync(
        int limit,
        string? before,
        CancellationToken cancellationToken = default);

    Task<NotificationPreferencesDto> GetPreferencesAsync(
        CancellationToken cancellationToken = default);

    Task<NotificationPreferencesDto> UpdatePreferencesAsync(
        UpdateNotificationPreferencesRequest request,
        CancellationToken cancellationToken = default);
}

public sealed class ParentDataService(ICloudApiClient api) : IParentDataService
{
    public async Task<IReadOnlyList<DailySummaryDto>> GetDailyAsync(
        DateOnly from,
        DateOnly to,
        CancellationToken cancellationToken = default)
    {
        var path = string.Create(
            CultureInfo.InvariantCulture,
            $"/v1/reports/daily?from={from:yyyy-MM-dd}&to={to:yyyy-MM-dd}");
        return await api.GetAsync<List<DailySummaryDto>>(
            path,
            cancellationToken: cancellationToken) ?? [];
    }

    public Task<NotificationListResponse> GetNotificationsAsync(
        int limit,
        string? before,
        CancellationToken cancellationToken = default)
    {
        var path = string.Create(CultureInfo.InvariantCulture, $"/v1/notifications?limit={limit}");
        if (!string.IsNullOrWhiteSpace(before))
            path += "&before=" + Uri.EscapeDataString(before);

        return api.GetAsync<NotificationListResponse>(path, cancellationToken: cancellationToken);
    }

    public Task<NotificationPreferencesDto> GetPreferencesAsync(
        CancellationToken cancellationToken = default) =>
        api.GetAsync<NotificationPreferencesDto>("/v1/preferences", cancellationToken: cancellationToken);

    public Task<NotificationPreferencesDto> UpdatePreferencesAsync(
        UpdateNotificationPreferencesRequest request,
        CancellationToken cancellationToken = default) =>
        api.PutAsync<UpdateNotificationPreferencesRequest, NotificationPreferencesDto>(
            "/v1/preferences",
            request,
            cancellationToken: cancellationToken);
}
