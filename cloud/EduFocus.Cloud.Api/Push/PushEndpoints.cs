using System.Net.WebSockets;
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
            .RequireAuthorization(AuthorizationPolicies.Read)
            .RequireRateLimiting(RateLimitPolicies.Token);
        endpoints.MapGet("/push/stream", StreamAsync)
            .RequireAuthorization()
            .RequireRateLimiting(RateLimitPolicies.Token);

        return endpoints;
    }

    private static IResult GetConfig(IOptions<PushOptions> options)
    {
        var pushOptions = options.Value;
        return Results.Ok(new PushConfigResponse(
            pushOptions.Enabled,
            pushOptions.Enabled ? pushOptions.VapidPublicKey : null,
            pushOptions.WebSocketEnabled));
    }

    private static async Task StreamAsync(
        HttpContext httpContext,
        CloudDbContext db,
        PushStreamRegistry registry,
        PushDispatchSignal dispatchSignal,
        IOptions<PushOptions> options,
        ILoggerFactory loggerFactory)
    {
        var pushOptions = options.Value;
        if (!pushOptions.WebSocketEnabled)
        {
            httpContext.Response.StatusCode = StatusCodes.Status404NotFound;
            return;
        }

        if (!httpContext.WebSockets.IsWebSocketRequest)
        {
            httpContext.Response.StatusCode = StatusCodes.Status400BadRequest;
            return;
        }

        if (httpContext.User.DeviceKind() != DeviceKind.Mobile)
        {
            httpContext.Response.StatusCode = StatusCodes.Status403Forbidden;
            return;
        }

        var deviceId = httpContext.User.DeviceId();
        var familyId = httpContext.User.FamilyId();

        using var socket = await httpContext.WebSockets.AcceptWebSocketAsync();
        var connection = registry.Register(deviceId, familyId, socket);

        // Re-check device & family status after registration to eliminate the race with concurrent revocation.
        var active = await db.Devices
            .AsNoTracking()
            .Join(
                db.Families.AsNoTracking(),
                d => d.FamilyId,
                f => f.Id,
                (d, f) => new { d.Id, d.FamilyId, d.RevokedAtUtc, f.DeletedAtUtc })
            .AnyAsync(
                x => x.Id == deviceId
                    && x.FamilyId == familyId
                    && x.RevokedAtUtc == null
                    && x.DeletedAtUtc == null,
                httpContext.RequestAborted);
        if (!active)
        {
            registry.Unregister(connection);
            connection.Abort();
            return;
        }

        await db.Notifications
            .Where(n => n.FamilyId == familyId
                && n.Status == NotificationStatus.Pending
                && n.FailureCode == "no_transport"
                && n.LastAttemptAtUtc != null)
            .ExecuteUpdateAsync(
                setters => setters.SetProperty(n => n.LastAttemptAtUtc, (DateTimeOffset?)null),
                httpContext.RequestAborted);
        dispatchSignal.Notify();

        var logger = loggerFactory.CreateLogger("PushStream");
        logger.LogInformation(
            "Push stream connected for device {DeviceId}.",
            connection.DeviceId);

        var heartbeatInterval = TimeSpan.FromSeconds(
            Math.Clamp(pushOptions.WebSocketHeartbeatSeconds, 5, 120));
        var heartbeatPayload = JsonSerializer.SerializeToUtf8Bytes(
            new Dictionary<string, string> { ["type"] = "heartbeat" });
        using var heartbeatCancellation = CancellationTokenSource.CreateLinkedTokenSource(
            httpContext.RequestAborted);
        var heartbeatTask = SendHeartbeatsAsync(
            connection,
            heartbeatPayload,
            heartbeatInterval,
            heartbeatCancellation.Token);

        try
        {
            var buffer = new byte[1024];
            while (socket.State == WebSocketState.Open)
            {
                var result = await socket.ReceiveAsync(
                    new ArraySegment<byte>(buffer),
                    httpContext.RequestAborted);
                if (result.MessageType == WebSocketMessageType.Close)
                {
                    await CloseOutputAsync(socket);
                    break;
                }

                await CloseWithPolicyViolationAsync(socket);
                break;
            }
        }
        catch (OperationCanceledException) when (httpContext.RequestAborted.IsCancellationRequested)
        {
        }
        catch (WebSocketException)
        {
        }
        finally
        {
            heartbeatCancellation.Cancel();
            registry.Unregister(connection);
            try
            {
                await heartbeatTask;
            }
            catch (OperationCanceledException)
            {
            }
            catch (Exception exception) when (
                exception is WebSocketException or ObjectDisposedException)
            {
            }

            await connection.CloseAsync();
            logger.LogInformation(
                "Push stream disconnected for device {DeviceId}.",
                connection.DeviceId);
        }
    }

    private static async Task SendHeartbeatsAsync(
        PushStreamConnection connection,
        ReadOnlyMemory<byte> payload,
        TimeSpan interval,
        CancellationToken cancellationToken)
    {
        while (!cancellationToken.IsCancellationRequested)
        {
            await Task.Delay(interval, cancellationToken);
            if (!await connection.TrySendAsync(payload, cancellationToken))
            {
                connection.Abort();
                return;
            }
        }
    }

    private static async Task CloseOutputAsync(WebSocket socket)
    {
        try
        {
            using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(2));
            await socket.CloseOutputAsync(
                WebSocketCloseStatus.NormalClosure,
                "closed",
                timeout.Token);
        }
        catch (Exception exception) when (
            exception is WebSocketException or ObjectDisposedException or OperationCanceledException)
        {
        }
    }

    private static async Task CloseWithPolicyViolationAsync(WebSocket socket)
    {
        try
        {
            using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(2));
            await socket.CloseAsync(
                WebSocketCloseStatus.PolicyViolation,
                "inbound_messages_not_supported",
                timeout.Token);
        }
        catch (Exception exception) when (
            exception is WebSocketException or ObjectDisposedException or OperationCanceledException)
        {
        }
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

        if (!httpContext.Request.HasJsonContentType())
        {
            return ApiResults.Error(
                StatusCodes.Status415UnsupportedMediaType,
                "unsupported_media_type",
                "Định dạng nội dung không được hỗ trợ.");
        }

        RegisterPushRequest? request;
        try
        {
            request = await httpContext.Request.ReadFromJsonAsync<RegisterPushRequest>(
                ApiJson.Options, cancellationToken);
        }
        catch (Exception exception) when (exception is JsonException or BadHttpRequestException)
        {
            return ApiResults.InvalidRequest("Body không đúng định dạng JSON.");
        }

        var pushOptions = options.Value;
        var enforceAllowedHosts = pushOptions.Enabled || pushOptions.AllowedEndpointHosts.Length > 0;
        if (!PushEndpointValidator.TryValidateRegistration(
                request?.Endpoint,
                request?.P256dh,
                request?.Auth,
                pushOptions.AllowedEndpointHosts,
                enforceAllowedHosts,
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
