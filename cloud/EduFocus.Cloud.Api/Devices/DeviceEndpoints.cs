using EduFocus.Cloud.Api.Auth;
using EduFocus.Cloud.Api.Common;
using EduFocus.Cloud.Api.RateLimiting;
using EduFocus.Cloud.Contracts.Common;
using EduFocus.Cloud.Contracts.Devices;
using EduFocus.Cloud.Data;
using EduFocus.Cloud.Data.Entities;
using Microsoft.EntityFrameworkCore;

namespace EduFocus.Cloud.Api.Devices;

public static class DeviceEndpoints
{
    public static IEndpointRouteBuilder MapDeviceEndpoints(this IEndpointRouteBuilder endpoints)
    {
        var group = endpoints.MapGroup("/devices");

        group.MapPost("/token/rotate", RotateTokenAsync)
            .RequireAuthorization()
            .RequireRateLimiting(RateLimitPolicies.Token);

        group.MapDelete("/{deviceId:guid}", RevokeDeviceAsync)
            .RequireAuthorization(AuthorizationPolicies.FamilyAdmin)
            .RequireRateLimiting(RateLimitPolicies.Token);

        return endpoints;
    }

    private static async Task<IResult> RotateTokenAsync(
        CloudDbContext db,
        HttpContext httpContext,
        CancellationToken cancellationToken)
    {
        var deviceId = httpContext.User.DeviceId();
        var device = await db.Devices
            .AsNoTracking()
            .FirstOrDefaultAsync(d => d.Id == deviceId, cancellationToken);
        if (device is null)
        {
            return ApiResults.NotFound();
        }

        var now = DateTimeOffset.UtcNow;
        var (token, hash) = DeviceTokenService.Create(device.Kind);
        var scopes = httpContext.User.Scopes();
        if (string.IsNullOrWhiteSpace(scopes))
        {
            scopes = device.Kind == DeviceKind.Desktop
                ? DeviceScopes.Desktop
                : DeviceScopes.Mobile;
        }

        await using var transaction = await db.Database.BeginTransactionAsync(cancellationToken);
        await db.DeviceTokens
            .Where(t => t.DeviceId == deviceId && t.RevokedAtUtc == null)
            .ExecuteUpdateAsync(
                setters => setters.SetProperty(t => t.RevokedAtUtc, now),
                cancellationToken);

        db.DeviceTokens.Add(new DeviceToken
        {
            Id = Guid.NewGuid(),
            DeviceId = deviceId,
            TokenHash = hash,
            Scopes = scopes,
            CreatedAtUtc = now,
        });
        db.CloudAuditLogs.Add(CloudAudit.Create(
            httpContext.User.FamilyId(),
            deviceId,
            "TokenRotated",
            now));
        await db.SaveChangesAsync(cancellationToken);
        await transaction.CommitAsync(cancellationToken);

        return Results.Ok(new RotateDeviceTokenResponse(token, now));
    }

    private static async Task<IResult> RevokeDeviceAsync(
        Guid deviceId,
        CloudDbContext db,
        HttpContext httpContext,
        CancellationToken cancellationToken)
    {
        var currentDeviceId = httpContext.User.DeviceId();
        var currentKind = httpContext.User.DeviceKind();
        var familyId = httpContext.User.FamilyId();

        var target = await db.Devices
            .FirstOrDefaultAsync(
                d => d.Id == deviceId && d.FamilyId == familyId,
                cancellationToken);
        if (target is null)
        {
            return ApiResults.NotFound();
        }

        if (target.Id != currentDeviceId
            && !(currentKind == DeviceKind.Desktop && target.Kind == DeviceKind.Mobile))
        {
            return ApiResults.Forbidden("Thiết bị không được phép thu hồi thiết bị này.");
        }

        if (target.RevokedAtUtc is null)
        {
            var now = DateTimeOffset.UtcNow;
            target.RevokedAtUtc = now;

            await db.DeviceTokens
                .Where(t => t.DeviceId == target.Id && t.RevokedAtUtc == null)
                .ExecuteUpdateAsync(
                    setters => setters.SetProperty(t => t.RevokedAtUtc, now),
                    cancellationToken);
            await db.PushRegistrations
                .Where(p => p.DeviceId == target.Id && p.InvalidatedAtUtc == null)
                .ExecuteUpdateAsync(
                    setters => setters.SetProperty(p => p.InvalidatedAtUtc, now),
                    cancellationToken);

            db.CloudAuditLogs.Add(CloudAudit.Create(
                familyId,
                currentDeviceId,
                "DeviceRevoked",
                now,
                new { revokedDeviceId = target.Id }));
            await db.SaveChangesAsync(cancellationToken);
        }

        return Results.Ok(new OperationResponse(true));
    }
}
