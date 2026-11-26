using System.Globalization;
using System.Threading.RateLimiting;
using EduFocus.Cloud.Api.Options;
using EduFocus.Cloud.Contracts.Common;
using Microsoft.AspNetCore.RateLimiting;

namespace EduFocus.Cloud.Api.RateLimiting;

public static class RateLimitPolicies
{
    public const string PairingCreate = "pairing-create";
    public const string PairingClaim = "pairing-claim";
    public const string Token = "token";
    public const string Sync = "sync";
}

public static class RateLimitSetup
{
    public static IServiceCollection AddCloudRateLimiting(this IServiceCollection services, RateLimitOptions limits)
    {
        services.AddRateLimiter(options =>
        {
            options.RejectionStatusCode = StatusCodes.Status429TooManyRequests;

            var globalPerMinute = Math.Max(limits.RequestsPerMinute * 4, 400);
            options.GlobalLimiter = PartitionedRateLimiter.Create<HttpContext, string>(context =>
                RateLimitPartition.GetFixedWindowLimiter(
                    $"global:{ClientKey(context)}",
                    _ => new FixedWindowRateLimiterOptions
                    {
                        PermitLimit = globalPerMinute,
                        Window = TimeSpan.FromMinutes(1),
                        QueueLimit = 0,
                        AutoReplenishment = true,
                    }));

            options.AddPolicy(RateLimitPolicies.PairingCreate, context =>
                FixedWindow($"create:{ClientKey(context)}", limits.PairingCreatePerHour, TimeSpan.FromHours(1)));

            options.AddPolicy(RateLimitPolicies.PairingClaim, context =>
                FixedWindow($"claim:{ClientKey(context)}", limits.PairingClaimPerHour, TimeSpan.FromHours(1)));

            options.AddPolicy(RateLimitPolicies.Token, context =>
                FixedWindow(TokenKey(context), limits.RequestsPerMinute, TimeSpan.FromMinutes(1)));

            options.AddPolicy(RateLimitPolicies.Sync, context =>
                FixedWindow($"sync:{TokenKey(context)}", limits.SyncRequestsPerMinute, TimeSpan.FromMinutes(1)));

            options.OnRejected = async (rejectedContext, cancellationToken) =>
            {
                var response = rejectedContext.HttpContext.Response;
                if (rejectedContext.Lease.TryGetMetadata(MetadataName.RetryAfter, out var retryAfter))
                {
                    response.Headers.RetryAfter = Math.Ceiling(retryAfter.TotalSeconds)
                        .ToString(CultureInfo.InvariantCulture);
                }

                await response.WriteAsJsonAsync(
                    new ApiErrorResponse(new ApiError(
                        "rate_limited",
                        "Quá nhiều yêu cầu. Vui lòng thử lại sau.")),
                    cancellationToken);
            };
        });

        return services;
    }

    private static RateLimitPartition<string> FixedWindow(string key, int permitLimit, TimeSpan window) =>
        RateLimitPartition.GetFixedWindowLimiter(key, _ => new FixedWindowRateLimiterOptions
        {
            PermitLimit = permitLimit,
            Window = window,
            QueueLimit = 0,
            AutoReplenishment = true,
        });

    private static string ClientKey(HttpContext context) =>
        context.Connection.RemoteIpAddress?.ToString() ?? "unknown";

    /// <summary>
    /// Partitions authenticated callers by verified device ID, falling back to client IP for unauthenticated traffic.
    /// </summary>
    public static string TokenKey(HttpContext context)
    {
        if (context.User.Identity?.IsAuthenticated == true)
        {
            var deviceId = context.User.FindFirst("device_id")?.Value;
            if (!string.IsNullOrWhiteSpace(deviceId))
            {
                return "device:" + deviceId;
            }
        }

        return "anonymous:" + ClientKey(context);
    }
}
