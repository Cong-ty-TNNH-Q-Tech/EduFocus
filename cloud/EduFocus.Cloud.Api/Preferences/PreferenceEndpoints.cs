using System.Text.Json;
using EduFocus.Cloud.Api.Auth;
using EduFocus.Cloud.Api.Common;
using EduFocus.Cloud.Api.Push;
using EduFocus.Cloud.Api.RateLimiting;
using EduFocus.Cloud.Contracts.Json;
using EduFocus.Cloud.Contracts.Preferences;
using EduFocus.Cloud.Data;
using EduFocus.Cloud.Data.Entities;
using Microsoft.EntityFrameworkCore;

namespace EduFocus.Cloud.Api.Preferences;

public static class PreferenceEndpoints
{
    public static IEndpointRouteBuilder MapPreferenceEndpoints(this IEndpointRouteBuilder endpoints)
    {
        var group = endpoints.MapGroup("/preferences");

        group.MapGet("/", GetAsync)
            .RequireAuthorization(AuthorizationPolicies.Read)
            .RequireRateLimiting(RateLimitPolicies.Token);

        group.MapPut("/", UpdateAsync)
            .RequireAuthorization(AuthorizationPolicies.Preferences)
            .RequireRateLimiting(RateLimitPolicies.Token);

        return endpoints;
    }

    private static async Task<IResult> GetAsync(
        CloudDbContext db,
        HttpContext httpContext,
        CancellationToken cancellationToken)
    {
        var familyId = httpContext.User.FamilyId();
        var family = await db.Families
            .AsNoTracking()
            .FirstOrDefaultAsync(f => f.Id == familyId, cancellationToken);
        if (family is null || family.DeletedAtUtc is not null)
        {
            return ApiResults.NotFound();
        }

        var preferences = await db.NotificationPreferences
            .AsNoTracking()
            .FirstOrDefaultAsync(p => p.FamilyId == familyId, cancellationToken);

        return Results.Ok(ToDto(preferences, family.TimeZoneId));
    }

    private static async Task<IResult> UpdateAsync(
        CloudDbContext db,
        HttpContext httpContext,
        CancellationToken cancellationToken)
    {
        UpdateNotificationPreferencesRequest? request;
        try
        {
            request = await httpContext.Request.ReadFromJsonAsync<UpdateNotificationPreferencesRequest>(
                ApiJson.Options, cancellationToken);
        }
        catch (JsonException)
        {
            return ApiResults.InvalidRequest("Body không đúng định dạng JSON.");
        }

        if (request is null)
        {
            return ApiResults.InvalidRequest("Body không hợp lệ.");
        }

        if (!IsValidMinutes(request.QuietStartMinutes)
            || !IsValidMinutes(request.QuietEndMinutes)
            || !IsValidMinutes(request.DailySummaryMinutes))
        {
            return ApiResults.Error(
                StatusCodes.Status400BadRequest,
                "invalid_preferences",
                "Giờ phút phải nằm trong khoảng 0–1439.");
        }

        if (request.TimeZoneId is null
            || request.TimeZoneId.Length > 64
            || !TimeZoneInfo.TryFindSystemTimeZoneById(request.TimeZoneId, out _))
        {
            return ApiResults.InvalidTimeZone();
        }

        var familyId = httpContext.User.FamilyId();
        var deviceId = httpContext.User.DeviceId();
        var family = await db.Families
            .FirstOrDefaultAsync(f => f.Id == familyId, cancellationToken);
        if (family is null || family.DeletedAtUtc is not null)
        {
            return ApiResults.NotFound();
        }

        var now = DateTimeOffset.UtcNow;
        var preferences = await db.NotificationPreferences
            .FirstOrDefaultAsync(p => p.FamilyId == familyId, cancellationToken);
        if (preferences is null)
        {
            preferences = new NotificationPreference { FamilyId = familyId };
            db.NotificationPreferences.Add(preferences);
        }

        var previous = new
        {
            preferences.AttentionAlert,
            preferences.DailySummary,
            preferences.ProtectionOff,
            preferences.SecurityAlert,
            preferences.BlockedAttempt,
            preferences.SecurityAlertBypassQuietHours,
            preferences.QuietStartMinutes,
            preferences.QuietEndMinutes,
            preferences.DailySummaryMinutes,
            preferences.TimeZoneId,
        };

        preferences.AttentionAlert = request.AttentionAlert;
        preferences.DailySummary = request.DailySummary;
        preferences.ProtectionOff = request.ProtectionOff;
        preferences.SecurityAlert = request.SecurityAlert;
        preferences.BlockedAttempt = request.BlockedAttempt;
        preferences.SecurityAlertBypassQuietHours = request.SecurityAlertBypassQuietHours;
        preferences.QuietStartMinutes = request.QuietStartMinutes;
        preferences.QuietEndMinutes = request.QuietEndMinutes;
        preferences.DailySummaryMinutes = request.DailySummaryMinutes;
        preferences.TimeZoneId = request.TimeZoneId;
        preferences.UpdatedAtUtc = now;

        if (family.TimeZoneId != request.TimeZoneId)
        {
            family.TimeZoneId = request.TimeZoneId;
        }

        db.CloudAuditLogs.Add(CloudAudit.Create(
            familyId,
            deviceId,
            "NotificationPreferencesUpdated",
            now,
            new
            {
                previous,
                current = new
                {
                    request.AttentionAlert,
                    request.DailySummary,
                    request.ProtectionOff,
                    request.SecurityAlert,
                    request.BlockedAttempt,
                    request.SecurityAlertBypassQuietHours,
                    request.QuietStartMinutes,
                    request.QuietEndMinutes,
                    request.DailySummaryMinutes,
                    request.TimeZoneId,
                },
            }));

        await db.SaveChangesAsync(cancellationToken);
        return Results.Ok(ToDto(preferences, family.TimeZoneId));
    }

    private static bool IsValidMinutes(int minutes) =>
        minutes is >= 0 and < PushRules.MinutesPerDay;

    private static NotificationPreferencesDto ToDto(
        NotificationPreference? preferences,
        string familyTimeZoneId) =>
        preferences is null
            ? new NotificationPreferencesDto(
                true,
                true,
                true,
                true,
                22 * 60,
                6 * 60,
                20 * 60 + 30,
                familyTimeZoneId,
                true,
                true)
            : new NotificationPreferencesDto(
                preferences.AttentionAlert,
                preferences.DailySummary,
                preferences.ProtectionOff,
                preferences.SecurityAlert,
                preferences.QuietStartMinutes,
                preferences.QuietEndMinutes,
                preferences.DailySummaryMinutes,
                preferences.TimeZoneId,
                preferences.SecurityAlertBypassQuietHours,
                preferences.BlockedAttempt);
}
