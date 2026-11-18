using System.Globalization;
using System.Threading.RateLimiting;
using EduFocus.Cloud.Api.Auth;
using EduFocus.Cloud.Api.Common;
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

    public static string TokenKey(HttpContext context)
    {
        var header = context.Request.Headers.Authorization.ToString();
        if (header.StartsWith("Bearer ", StringComparison.OrdinalIgnoreCase))
        {
            var token = header["Bearer ".Length..].Trim();
            if (DeviceTokenService.TryHash(token, out var hash))
            {
                return "token:" + hash;
            }
        }

        return "anonymous:" + ClientKey(context);
    }
}
