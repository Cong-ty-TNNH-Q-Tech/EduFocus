using EduFocus.Cloud.Api.Auth;
using EduFocus.Cloud.Api.Common;
using EduFocus.Cloud.Api.RateLimiting;
using EduFocus.Cloud.Contracts.Pairing;
using EduFocus.Cloud.Data;
using EduFocus.Cloud.Data.Entities;
using Microsoft.EntityFrameworkCore;

namespace EduFocus.Cloud.Api.Link;

public static class LinkEndpoints
{
    public static IEndpointRouteBuilder MapLinkEndpoints(this IEndpointRouteBuilder endpoints)
    {
        endpoints.MapGet("/link/status", GetLinkStatusAsync)
            .RequireAuthorization()
            .RequireRateLimiting(RateLimitPolicies.Token);

        return endpoints;
    }

    private static async Task<IResult> GetLinkStatusAsync(
        CloudDbContext db,
        HttpContext httpContext,
        CancellationToken cancellationToken)
    {
        var familyId = httpContext.User.FamilyId();
        var deviceId = httpContext.User.DeviceId();

        var family = await db.Families
            .AsNoTracking()
            .FirstOrDefaultAsync(f => f.Id == familyId, cancellationToken);
        if (family is null || family.DeletedAtUtc is not null)
        {
            return ApiResults.NotFound();
        }

        var devices = (await db.Devices
                .AsNoTracking()
                .Where(d => d.FamilyId == familyId)
                .OrderBy(d => d.CreatedAtUtc)
                .ToListAsync(cancellationToken))
            .Select(d => new LinkedDeviceDto(
                d.Id,
                d.Kind.ToString(),
                d.DisplayName,
                d.Platform,
                d.CreatedAtUtc,
                d.LastSeenAtUtc,
                d.RevokedAtUtc))
            .ToList();

        var lastSyncAtUtc = await db.SyncEvents
            .AsNoTracking()
            .Where(e => e.FamilyId == familyId)
            .OrderByDescending(e => e.ReceivedAtUtc)
            .Select(e => (DateTimeOffset?)e.ReceivedAtUtc)
            .FirstOrDefaultAsync(cancellationToken);

        var pendingEvents = await db.SyncEvents
            .CountAsync(
                e => e.FamilyId == familyId && e.Status == SyncEventStatus.Pending,
                cancellationToken);

        return Results.Ok(new LinkStatusResponse(
            true,
            family.Id,
            deviceId,
            family.TimeZoneId,
            family.CreatedAtUtc,
            lastSyncAtUtc,
            pendingEvents,
            devices));
    }
}
