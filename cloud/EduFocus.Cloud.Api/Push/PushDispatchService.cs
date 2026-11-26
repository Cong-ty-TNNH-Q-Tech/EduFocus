using EduFocus.Cloud.Api.Options;
using EduFocus.Cloud.Data;
using EduFocus.Cloud.Data.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;

namespace EduFocus.Cloud.Api.Push;

public sealed class PushDispatchService(
    CloudDbContext db,
    IPushSender sender,
    PushStreamRegistry streams,
    IOptions<PushOptions> options,
    TimeProvider timeProvider,
    ILogger<PushDispatchService> logger)
{
    public async Task<int> DispatchPendingAsync(CancellationToken cancellationToken)
    {
        var now = timeProvider.GetUtcNow();
        var settings = options.Value;
        var cutoff1 = now - PushRules.RetryBackoff(1);
        var cutoff2 = now - PushRules.RetryBackoff(2);
        var cutoff3 = now - PushRules.RetryBackoff(3);
        var cutoff4 = now - PushRules.RetryBackoff(4);
        var cutoff5 = now - PushRules.RetryBackoff(5);

        var candidates = await db.Notifications
            .Where(n => n.Status == NotificationStatus.Pending
                && (n.LastAttemptAtUtc == null
                    || (n.AttemptCount <= 1 && n.LastAttemptAtUtc <= cutoff1)
                    || (n.AttemptCount == 2 && n.LastAttemptAtUtc <= cutoff2)
                    || (n.AttemptCount == 3 && n.LastAttemptAtUtc <= cutoff3)
                    || (n.AttemptCount == 4 && n.LastAttemptAtUtc <= cutoff4)
                    || (n.AttemptCount >= 5 && n.LastAttemptAtUtc <= cutoff5)))
            .OrderBy(n => n.CreatedAtUtc)
            .Take(settings.BatchSize)
            .ToListAsync(cancellationToken);

        var processed = 0;
        foreach (var notification in candidates)
        {
            if (notification.LastAttemptAtUtc is { } lastAttempt
                && now < lastAttempt + PushRules.RetryBackoff(notification.AttemptCount))
            {
                continue;
            }

            await DispatchOneAsync(notification, now, settings, cancellationToken);
            processed++;
        }

        return processed;
    }

    private async Task DispatchOneAsync(
        NotificationRecord notification,
        DateTimeOffset now,
        PushOptions settings,
        CancellationToken cancellationToken)
    {
        var family = await db.Families
            .AsNoTracking()
            .FirstOrDefaultAsync(
                f => f.Id == notification.FamilyId && f.DeletedAtUtc == null,
                cancellationToken);
        if (family is null)
        {
            Suppress(notification, "family_unavailable");
            await db.SaveChangesAsync(cancellationToken);
            return;
        }

        var preferences = await db.NotificationPreferences
            .AsNoTracking()
            .FirstOrDefaultAsync(p => p.FamilyId == notification.FamilyId, cancellationToken);

        if (!PushRules.IsEnabled(preferences, notification.Category))
        {
            Suppress(notification, "preference_disabled");
            await db.SaveChangesAsync(cancellationToken);
            return;
        }

        var timeZoneId = preferences?.TimeZoneId ?? family.TimeZoneId;
        var quietStart = preferences?.QuietStartMinutes ?? 22 * 60;
        var quietEnd = preferences?.QuietEndMinutes ?? 6 * 60;
        if (!PushRules.BypassesQuietHours(preferences, notification.Category)
            && PushRules.IsQuietHours(now, timeZoneId, quietStart, quietEnd))
        {
            Suppress(notification, "quiet_hours");
            await db.SaveChangesAsync(cancellationToken);
            return;
        }

        var streamConnections = settings.WebSocketEnabled
            ? streams.GetConnections(notification.FamilyId)
            : Array.Empty<PushStreamConnection>();
        var registrations = await (
                from registration in db.PushRegistrations.AsNoTracking()
                join device in db.Devices.AsNoTracking() on registration.DeviceId equals device.Id
                where device.FamilyId == notification.FamilyId
                    && device.Kind == DeviceKind.Mobile
                    && device.RevokedAtUtc == null
                    && registration.InvalidatedAtUtc == null
                select new
                {
                    registration.DeviceId,
                    registration.Endpoint,
                    registration.P256dh,
                    registration.Auth,
                })
            .Distinct()
            .ToListAsync(cancellationToken);

        if (streamConnections.Count == 0 && registrations.Count == 0)
        {
            if (settings.WebSocketEnabled
                && await db.Devices.AsNoTracking().AnyAsync(
                    d => d.FamilyId == notification.FamilyId
                        && d.Kind == DeviceKind.Mobile
                        && d.RevokedAtUtc == null,
                    cancellationToken))
            {
                if (notification.FailureCode != "no_transport"
                    || notification.LastAttemptAtUtc is not null)
                {
                    notification.FailureCode = "no_transport";
                    notification.LastAttemptAtUtc = null;
                    await db.SaveChangesAsync(cancellationToken);
                }

                return;
            }

            Suppress(notification, "no_device");
            await db.SaveChangesAsync(cancellationToken);
            return;
        }

        notification.AttemptCount++;
        notification.LastAttemptAtUtc = now;
        var data = PushRules.BuildPushData(notification);
        var results = new List<PushSendResult>();
        var streamSuccessDeviceIds = new HashSet<Guid>();

        if (streamConnections.Count > 0)
        {
            IReadOnlyList<PushStreamSendResult> streamResults;
            try
            {
                streamResults = await streams.SendAsync(
                    notification.FamilyId,
                    data,
                    cancellationToken);
            }
            catch (OperationCanceledException)
            {
                throw;
            }
            catch (Exception exception)
            {
                logger.LogError(
                    "Push stream send failed for notification {NotificationId}: {ErrorType}",
                    notification.Id,
                    exception.GetType().Name);
                streamResults = streamConnections
                    .Select(connection => new PushStreamSendResult(connection.DeviceId, false))
                    .ToList();
            }

            foreach (var streamResult in streamResults)
            {
                if (streamResult.Success)
                {
                    streamSuccessDeviceIds.Add(streamResult.DeviceId);
                }

                results.Add(new PushSendResult(
                    $"wss:{streamResult.DeviceId:D}",
                    streamResult.Success,
                    false,
                    streamResult.Success ? null : "stream_unavailable"));
            }
        }

        var targets = registrations
            .Where(registration => !streamSuccessDeviceIds.Contains(registration.DeviceId))
            .Select(registration => new PushTarget(
                registration.Endpoint,
                registration.P256dh,
                registration.Auth))
            .ToList();

        if (targets.Count > 0)
        {
            try
            {
                results.AddRange(await sender.SendAsync(targets, data, cancellationToken));
            }
            catch (OperationCanceledException)
            {
                throw;
            }
            catch (Exception exception)
            {
                logger.LogError(
                    "Push send failed for notification {NotificationId}: {ErrorType}",
                    notification.Id,
                    exception.GetType().Name);
                results.AddRange(targets.Select(target => new PushSendResult(
                    target.Endpoint,
                    false,
                    false,
                    "push_unavailable")));
            }
        }

        var invalidEndpoints = results
            .Where(r => r.EndpointInvalid)
            .Select(r => r.Endpoint)
            .ToList();
        if (invalidEndpoints.Count > 0)
        {
            await db.PushRegistrations
                .Where(p => invalidEndpoints.Contains(p.Endpoint) && p.InvalidatedAtUtc == null)
                .ExecuteUpdateAsync(
                    setters => setters.SetProperty(p => p.InvalidatedAtUtc, now),
                    cancellationToken);
        }

        if (results.Any(r => r.Success))
        {
            notification.Status = NotificationStatus.Sent;
            notification.SentAtUtc = now;
            notification.FailureCode = null;
        }
        else
        {
            var allInvalid = results.Count > 0 && results.All(r => r.EndpointInvalid);
            notification.FailureCode = allInvalid
                ? "endpoint_invalid"
                : NormalizeCode(results.FirstOrDefault()?.ErrorCode);
            notification.Status = allInvalid || notification.AttemptCount >= settings.MaxAttempts
                ? NotificationStatus.Failed
                : NotificationStatus.Pending;
        }

        await db.SaveChangesAsync(cancellationToken);
    }

    private static void Suppress(NotificationRecord notification, string reason)
    {
        notification.Status = NotificationStatus.Suppressed;
        notification.FailureCode = reason;
    }

    private static string NormalizeCode(string? code)
    {
        if (string.IsNullOrWhiteSpace(code))
        {
            return "push_unavailable";
        }

        var normalized = code.Replace("_", string.Empty).Replace("-", string.Empty).ToLowerInvariant();
        return normalized.Length <= 64 ? normalized : normalized[..64];
    }
}
