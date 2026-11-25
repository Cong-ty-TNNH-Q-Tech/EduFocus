using System.Text.Json;
using EduFocus.Cloud.Api.Auth;
using EduFocus.Cloud.Api.Common;
using EduFocus.Cloud.Api.Options;
using EduFocus.Cloud.Api.RateLimiting;
using EduFocus.Cloud.Contracts.Json;
using EduFocus.Cloud.Contracts.Push;
using EduFocus.Cloud.Data;
using EduFocus.Cloud.Data.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;

namespace EduFocus.Cloud.Api.Push;

public static class PushEndpoints
{
    public const int MaxPlatformLength = 32;
    public const int MaxAppVersionLength = 32;

    public static IEndpointRouteBuilder MapPushEndpoints(this IEndpointRouteBuilder endpoints)
    {
        endpoints.MapPost("/push/registrations", RegisterAsync)
            .RequireAuthorization()
            .RequireRateLimiting(RateLimitPolicies.Token);
        endpoints.MapGet("/push/config", GetConfig)
            .RequireAuthorization()
            .RequireRateLimiting(RateLimitPolicies.Token);

        return endpoints;
    }

    private static IResult GetConfig(IOptions<PushOptions> options)
    {
        var pushOptions = options.Value;
        return Results.Ok(new PushConfigResponse(
            pushOptions.Enabled,
            pushOptions.Enabled ? pushOptions.VapidPublicKey : null));
    }

    private static async Task<IResult> RegisterAsync(
        HttpContext httpContext,
        CloudDbContext db,
        IOptions<PushOptions> options,
        CancellationToken cancellationToken)
    {
        if (httpContext.User.DeviceKind() != DeviceKind.Mobile)
        {
            return ApiResults.Forbidden("Chỉ thiết bị di động đăng ký nhận thông báo.");
        }

        RegisterPushRequest? request;
        try
        {
            request = await httpContext.Request.ReadFromJsonAsync<RegisterPushRequest>(
                ApiJson.Options, cancellationToken);
        }
        catch (JsonException)
        {
            return ApiResults.InvalidRequest("Body không đúng định dạng JSON.");
        }

        var pushOptions = options.Value;
        if (!PushEndpointValidator.TryValidateRegistration(
                request?.Endpoint,
                request?.P256dh,
                request?.Auth,
                pushOptions.AllowedEndpointHosts,
                pushOptions.Enabled,
                out var endpoint,
                out var p256dh,
                out var auth))
        {
            return ApiResults.InvalidRequest("Đăng ký thông báo không hợp lệ.");
        }

        var platform = request!.Platform?.Trim();
        if (string.IsNullOrWhiteSpace(platform) || platform.Length > MaxPlatformLength)
        {
            return ApiResults.InvalidRequest("Nền tảng không hợp lệ.");
        }

        var appVersion = request.AppVersion?.Trim();
        if (appVersion is { Length: > MaxAppVersionLength })
        {
            return ApiResults.InvalidRequest("Phiên bản ứng dụng không hợp lệ.");
        }

        var deviceId = httpContext.User.DeviceId();
        var familyId = httpContext.User.FamilyId();
        var device = await db.Devices
            .FirstOrDefaultAsync(
                d => d.Id == deviceId && d.FamilyId == familyId && d.RevokedAtUtc == null,
                cancellationToken);
        if (device is null)
        {
            return ApiResults.NotFound();
        }

        var now = DateTimeOffset.UtcNow;
        device.LastSeenAtUtc = now;
        if (!string.IsNullOrWhiteSpace(appVersion))
        {
            device.AppVersion = appVersion;
        }

        await db.PushRegistrations
            .Where(p => p.Endpoint == endpoint
                && p.DeviceId != deviceId
                && p.InvalidatedAtUtc == null)
            .ExecuteUpdateAsync(
                setters => setters.SetProperty(p => p.InvalidatedAtUtc, now),
                cancellationToken);

        var registration = await db.PushRegistrations
            .FirstOrDefaultAsync(p => p.DeviceId == deviceId, cancellationToken);
        if (registration is null)
        {
            registration = new PushRegistration
            {
                Id = Guid.NewGuid(),
                DeviceId = deviceId,
            };
            db.PushRegistrations.Add(registration);
        }

        registration.Endpoint = endpoint;
        registration.P256dh = p256dh;
        registration.Auth = auth;
        registration.Platform = platform;
        registration.UpdatedAtUtc = now;
        registration.InvalidatedAtUtc = null;

        db.CloudAuditLogs.Add(CloudAudit.Create(
            familyId,
            deviceId,
            "PushRegistrationUpdated",
            now,
            new { platform }));

        await db.SaveChangesAsync(cancellationToken);
        return Results.Ok(new RegisterPushResponse(now));
    }
}
