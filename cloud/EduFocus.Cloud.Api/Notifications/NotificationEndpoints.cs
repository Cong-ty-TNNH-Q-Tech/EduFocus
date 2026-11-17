using System.Buffers.Text;
using System.Globalization;
using System.Text;
using EduFocus.Cloud.Api.Auth;
using EduFocus.Cloud.Api.Common;
using EduFocus.Cloud.Api.RateLimiting;
using EduFocus.Cloud.Contracts.Notifications;
using EduFocus.Cloud.Data;
using EduFocus.Cloud.Data.Entities;
using Microsoft.EntityFrameworkCore;
using ContractKind = EduFocus.Cloud.Contracts.Notifications.NotificationKind;

namespace EduFocus.Cloud.Api.Notifications;

public static class NotificationEndpoints
{
    public const int DefaultLimit = 50;
    public const int MaxLimit = 100;

    public static IEndpointRouteBuilder MapNotificationEndpoints(this IEndpointRouteBuilder endpoints)
    {
        endpoints.MapGet("/notifications", ListAsync)
            .RequireAuthorization(AuthorizationPolicies.Read)
            .RequireRateLimiting(RateLimitPolicies.Token);

        return endpoints;
    }

    private static async Task<IResult> ListAsync(
        int? limit,
        string? before,
        CloudDbContext db,
        HttpContext httpContext,
        CancellationToken cancellationToken)
    {
        var take = limit ?? DefaultLimit;
        if (take is < 1 or > MaxLimit)
        {
            return ApiResults.Error(
                StatusCodes.Status400BadRequest,
                "invalid_limit",
                $"limit phải nằm trong khoảng 1–{MaxLimit}.");
        }

        var familyId = httpContext.User.FamilyId();
        var family = await db.Families
            .AsNoTracking()
            .FirstOrDefaultAsync(f => f.Id == familyId, cancellationToken);
        if (family is null || family.DeletedAtUtc is not null)
        {
            return ApiResults.NotFound();
        }

        var query = db.Notifications
            .AsNoTracking()
            .Where(n => n.FamilyId == familyId);

        if (!string.IsNullOrWhiteSpace(before))
        {
            if (!TryDecodeCursor(before, out var cursorTime, out var cursorId))
            {
                return ApiResults.Error(
                    StatusCodes.Status400BadRequest,
                    "invalid_cursor",
                    "Cursor không hợp lệ.");
            }

            query = query.Where(n => n.CreatedAtUtc < cursorTime
                || (n.CreatedAtUtc == cursorTime && n.Id.CompareTo(cursorId) < 0));
        }

        var items = await query
            .OrderByDescending(n => n.CreatedAtUtc)
            .ThenByDescending(n => n.Id)
            .Take(take + 1)
            .ToListAsync(cancellationToken);

        var hasMore = items.Count > take;
        var page = items.Take(take).ToList();
        var next = hasMore ? EncodeCursor(page[^1]) : null;

        return Results.Ok(new NotificationListResponse(
            page.Select(n => new NotificationDto(
                n.Id,
                MapKind(n.Category),
                MapStatus(n.Status),
                n.CreatedAtUtc,
                n.SentAtUtc)).ToList(),
            next));
    }

    private static string EncodeCursor(NotificationRecord notification) =>
        Base64Url.EncodeToString(Encoding.UTF8.GetBytes(
            $"{notification.CreatedAtUtc.UtcTicks}:{notification.Id:N}"));

    private static bool TryDecodeCursor(
        string value,
        out DateTimeOffset createdAtUtc,
        out Guid id)
    {
        createdAtUtc = default;
        id = default;

        byte[] bytes;
        try
        {
            bytes = Base64Url.DecodeFromChars(value.AsSpan());
        }
        catch (FormatException)
        {
            return false;
        }

        var text = Encoding.UTF8.GetString(bytes);
        var separator = text.IndexOf(':', StringComparison.Ordinal);
        if (separator <= 0)
        {
            return false;
        }

        if (!long.TryParse(
                text.AsSpan(0, separator),
                NumberStyles.None,
                CultureInfo.InvariantCulture,
                out var ticks))
        {
            return false;
        }

        if (ticks < DateTimeOffset.MinValue.UtcTicks
            || ticks > DateTimeOffset.MaxValue.UtcTicks
            || !Guid.TryParseExact(text.AsSpan(separator + 1), "N", out id))
        {
            return false;
        }

        createdAtUtc = new DateTimeOffset(ticks, TimeSpan.Zero);
        return true;
    }

    private static ContractKind MapKind(NotificationCategory category) => category switch
    {
        NotificationCategory.DailySummary => ContractKind.DailySummary,
        NotificationCategory.AttentionAlert => ContractKind.AttentionAlert,
        NotificationCategory.ProtectionOff => ContractKind.ProtectionOff,
        NotificationCategory.SecurityAlert => ContractKind.SecurityAlert,
        _ => throw new ArgumentOutOfRangeException(nameof(category)),
    };

    private static NotificationDeliveryStatus MapStatus(NotificationStatus status) => status switch
    {
        NotificationStatus.Pending => NotificationDeliveryStatus.Pending,
        NotificationStatus.Sent => NotificationDeliveryStatus.Sent,
        NotificationStatus.Suppressed => NotificationDeliveryStatus.Suppressed,
        NotificationStatus.Failed => NotificationDeliveryStatus.Failed,
        _ => throw new ArgumentOutOfRangeException(nameof(status)),
    };
}
