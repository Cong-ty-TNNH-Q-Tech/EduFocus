using EduFocus.Cloud.Api.Auth;
using EduFocus.Cloud.Api.Common;
using EduFocus.Cloud.Api.Options;
using EduFocus.Cloud.Api.RateLimiting;
using EduFocus.Cloud.Contracts.Pairing;
using EduFocus.Cloud.Data;
using EduFocus.Cloud.Data.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;

namespace EduFocus.Cloud.Api.Pairing;

public static class PairingEndpoints
{
    private const int PairingLifetimeMinutes = 10;
    private const int MaxClaimAttempts = 5;
    private const int MaxCodeAttempts = 5;

    public static IEndpointRouteBuilder MapPairingEndpoints(this IEndpointRouteBuilder endpoints)
    {
        var group = endpoints.MapGroup("/pairing");

        group.MapPost("/sessions", CreateSessionAsync)
            .RequireRateLimiting(RateLimitPolicies.PairingCreate)
            .AllowAnonymous();

        group.MapPost("/claim", ClaimAsync)
            .RequireRateLimiting(RateLimitPolicies.PairingClaim)
            .AllowAnonymous();

        group.MapGet("/sessions/{sessionId:guid}", GetSessionStatusAsync)
            .RequireAuthorization(AuthorizationPolicies.FamilyAdmin)
            .RequireRateLimiting(RateLimitPolicies.Token);

        return endpoints;
    }

    private static async Task<IResult> CreateSessionAsync(
        CreatePairingSessionRequest request,
        CloudDbContext db,
        IOptions<CloudApiOptions> apiOptions,
        CancellationToken cancellationToken)
    {
        var desktopName = request.DesktopName?.Trim();
        if (string.IsNullOrWhiteSpace(desktopName) || desktopName.Length > 128)
        {
            return ApiResults.InvalidRequest("Tên thiết bị không hợp lệ.");
        }

        var platform = request.Platform?.Trim();
        if (string.IsNullOrWhiteSpace(platform) || platform.Length > 32)
        {
            return ApiResults.InvalidRequest("Nền tảng không hợp lệ.");
        }

        var appVersion = request.AppVersion?.Trim();
        if (appVersion is { Length: > 32 })
        {
            return ApiResults.InvalidRequest("Phiên bản ứng dụng không hợp lệ.");
        }

        if (!TimeZones.TryNormalize(request.TimeZoneId, out var timeZoneId))
        {
            return ApiResults.InvalidTimeZone();
        }

        var now = DateTimeOffset.UtcNow;
        var family = new Family
        {
            Id = Guid.NewGuid(),
            CreatedAtUtc = now,
            TimeZoneId = timeZoneId,
        };
        var desktop = new Device
        {
            Id = Guid.NewGuid(),
            FamilyId = family.Id,
            Kind = DeviceKind.Desktop,
            DisplayName = desktopName,
            Platform = platform,
            AppVersion = appVersion,
            CreatedAtUtc = now,
            LastSeenAtUtc = now,
        };
        var (desktopToken, desktopTokenHash) = DeviceTokenService.Create(DeviceKind.Desktop);
        var desktopTokenEntity = new DeviceToken
        {
            Id = Guid.NewGuid(),
            DeviceId = desktop.Id,
            TokenHash = desktopTokenHash,
            Scopes = DeviceScopes.Desktop,
            CreatedAtUtc = now,
        };

        var code = PairingCode.Create();
        var session = new PairingSession
        {
            Id = Guid.NewGuid(),
            FamilyId = family.Id,
            DesktopDeviceId = desktop.Id,
            CodeHash = PairingCode.Hash(code),
            CreatedAtUtc = now,
            ExpiresAtUtc = now.AddMinutes(PairingLifetimeMinutes),
        };

        db.Families.Add(family);
        db.Devices.Add(desktop);
        db.DeviceTokens.Add(desktopTokenEntity);
        db.PairingSessions.Add(session);
        db.NotificationPreferences.Add(new NotificationPreference
        {
            FamilyId = family.Id,
            TimeZoneId = timeZoneId,
            UpdatedAtUtc = now,
        });
        db.CloudAuditLogs.Add(CloudAudit.Create(
            family.Id,
            desktop.Id,
            "PairingCreated",
            now,
            new { sessionId = session.Id }));

        for (var attempt = 0; ; attempt++)
        {
            try
            {
                await db.SaveChangesAsync(cancellationToken);
                break;
            }
            catch (DbUpdateException) when (attempt < MaxCodeAttempts - 1)
            {
                code = PairingCode.Create();
                session.CodeHash = PairingCode.Hash(code);
            }
        }

        var cloudBaseUrl = apiOptions.Value.PublicBaseUrl.TrimEnd('/');
        var pairingUri = PairingUri.Build(cloudBaseUrl, session.Id, code);

        return Results.Ok(new PairingSessionResponse(
            session.Id,
            code,
            pairingUri,
            session.ExpiresAtUtc,
            desktopToken,
            cloudBaseUrl));
    }

    private static async Task<IResult> ClaimAsync(
        ClaimPairingRequest request,
        CloudDbContext db,
        CancellationToken cancellationToken)
    {
        if (!PairingCode.TryNormalize(request.Code, out var normalizedCode))
        {
            return ApiResults.PairingUnavailable();
        }

        var deviceName = request.DeviceName?.Trim();
        if (string.IsNullOrWhiteSpace(deviceName) || deviceName.Length > 128)
        {
            return ApiResults.InvalidRequest("Tên thiết bị không hợp lệ.");
        }

        var platform = request.Platform?.Trim();
        if (string.IsNullOrWhiteSpace(platform) || platform.Length > 32)
        {
            return ApiResults.InvalidRequest("Nền tảng không hợp lệ.");
        }

        var appVersion = request.AppVersion?.Trim();
        if (appVersion is { Length: > 32 })
        {
            return ApiResults.InvalidRequest("Phiên bản ứng dụng không hợp lệ.");
        }

        if (!TimeZones.TryNormalize(request.TimeZoneId, out _))
        {
            return ApiResults.InvalidTimeZone();
        }

        var now = DateTimeOffset.UtcNow;
        var codeHash = PairingCode.Hash(normalizedCode);
        PairingSession? session;
        if (!string.IsNullOrWhiteSpace(request.SessionId))
        {
            if (!Guid.TryParse(request.SessionId, out var requestedSessionId))
            {
                return ApiResults.PairingUnavailable();
            }

            session = await db.PairingSessions
                .FirstOrDefaultAsync(s => s.Id == requestedSessionId, cancellationToken);
            if (session is null)
            {
                return ApiResults.PairingUnavailable();
            }

            if (session.CodeHash != codeHash)
            {
                await IncrementAttemptAsync(db, session.Id, cancellationToken);
                return ApiResults.PairingUnavailable();
            }
        }
        else
        {
            session = await db.PairingSessions
                .FirstOrDefaultAsync(
                    s => s.CodeHash == codeHash && s.ClaimedAtUtc == null,
                    cancellationToken);
            if (session is null)
            {
                return ApiResults.PairingUnavailable();
            }
        }

        if (session.ClaimedAtUtc is not null
            || session.ExpiresAtUtc <= now
            || session.AttemptCount >= MaxClaimAttempts)
        {
            return ApiResults.PairingUnavailable();
        }

        var desktop = await db.Devices
            .AsNoTracking()
            .FirstOrDefaultAsync(d => d.Id == session.DesktopDeviceId, cancellationToken);
        var family = await db.Families
            .AsNoTracking()
            .FirstOrDefaultAsync(f => f.Id == session.FamilyId, cancellationToken);
        if (desktop is null || family is null || family.DeletedAtUtc is not null)
        {
            return ApiResults.PairingUnavailable();
        }

        var mobile = new Device
        {
            Id = Guid.NewGuid(),
            FamilyId = family.Id,
            Kind = DeviceKind.Mobile,
            DisplayName = deviceName,
            Platform = platform,
            AppVersion = appVersion,
            CreatedAtUtc = now,
            LastSeenAtUtc = now,
        };
        var (mobileToken, mobileTokenHash) = DeviceTokenService.Create(DeviceKind.Mobile);
        var mobileTokenEntity = new DeviceToken
        {
            Id = Guid.NewGuid(),
            DeviceId = mobile.Id,
            TokenHash = mobileTokenHash,
            Scopes = DeviceScopes.Mobile,
            CreatedAtUtc = now,
        };

        await using var transaction = await db.Database.BeginTransactionAsync(cancellationToken);
        db.Devices.Add(mobile);
        db.DeviceTokens.Add(mobileTokenEntity);
        db.CloudAuditLogs.Add(CloudAudit.Create(
            family.Id,
            desktop.Id,
            "PairingClaimed",
            now,
            new { sessionId = session.Id, mobileDeviceId = mobile.Id }));
        await db.SaveChangesAsync(cancellationToken);

        var claimed = await db.PairingSessions
            .Where(s => s.Id == session.Id
                && s.ClaimedAtUtc == null
                && s.ExpiresAtUtc > now
                && s.AttemptCount < MaxClaimAttempts)
            .ExecuteUpdateAsync(
                setters => setters
                    .SetProperty(s => s.ClaimedAtUtc, now)
                    .SetProperty(s => s.ClaimedByDeviceId, mobile.Id),
                cancellationToken);
        if (claimed == 0)
        {
            await transaction.RollbackAsync(cancellationToken);
            return ApiResults.PairingUnavailable();
        }

        await transaction.CommitAsync(cancellationToken);

        return Results.Ok(new ClaimPairingResponse(
            mobileToken,
            family.Id,
            mobile.Id,
            desktop.DisplayName,
            family.TimeZoneId,
            now));
    }

    private static async Task<IResult> GetSessionStatusAsync(
        Guid sessionId,
        CloudDbContext db,
        HttpContext httpContext,
        CancellationToken cancellationToken)
    {
        var deviceId = httpContext.User.DeviceId();
        var session = await db.PairingSessions
            .AsNoTracking()
            .FirstOrDefaultAsync(
                s => s.Id == sessionId && s.DesktopDeviceId == deviceId,
                cancellationToken);
        if (session is null)
        {
            return ApiResults.NotFound();
        }

        string? mobileName = null;
        if (session.ClaimedByDeviceId is { } mobileDeviceId)
        {
            mobileName = await db.Devices
                .AsNoTracking()
                .Where(d => d.Id == mobileDeviceId)
                .Select(d => d.DisplayName)
                .FirstOrDefaultAsync(cancellationToken);
        }

        var now = DateTimeOffset.UtcNow;
        var status = session.ClaimedAtUtc is not null
            ? PairingSessionStatus.Claimed
            : session.ExpiresAtUtc <= now
                ? PairingSessionStatus.Expired
                : PairingSessionStatus.Pending;

        return Results.Ok(new PairingSessionStatusResponse(
            session.Id,
            status,
            session.ExpiresAtUtc,
            session.ClaimedAtUtc,
            mobileName,
            session.ClaimedByDeviceId));
    }

    private static async Task IncrementAttemptAsync(
        CloudDbContext db,
        Guid sessionId,
        CancellationToken cancellationToken) =>
        await db.PairingSessions
            .Where(s => s.Id == sessionId)
            .ExecuteUpdateAsync(
                setters => setters.SetProperty(s => s.AttemptCount, s => s.AttemptCount + 1),
                cancellationToken);
}
