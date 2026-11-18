using System.Text.Json;
using EduFocus.Cloud.Api.Auth;
using EduFocus.Cloud.Api.Common;
using EduFocus.Cloud.Api.Options;
using EduFocus.Cloud.Api.Push;
using EduFocus.Cloud.Api.RateLimiting;
using EduFocus.Cloud.Contracts.Json;
using EduFocus.Cloud.Contracts.Sync;
using EduFocus.Cloud.Data;
using EduFocus.Cloud.Data.Entities;
using Microsoft.AspNetCore.Http.Features;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using Npgsql;
using ContractEventType = EduFocus.Cloud.Contracts.Sync.SyncEventType;
using EntityEventType = EduFocus.Cloud.Data.Entities.SyncEventType;

namespace EduFocus.Cloud.Api.Sync;

public static class SyncEndpoints
{
    public static IEndpointRouteBuilder MapSyncEndpoints(this IEndpointRouteBuilder endpoints)
    {
        endpoints.MapPost("/sync/events", IngestAsync)
            .RequireAuthorization(AuthorizationPolicies.Sync)
            .RequireRateLimiting(RateLimitPolicies.Sync);

        return endpoints;
    }

    private static async Task<IResult> IngestAsync(
        HttpContext httpContext,
        CloudDbContext db,
        IOptions<SyncOptions> syncOptions,
        CancellationToken cancellationToken)
    {
        var options = syncOptions.Value;
        var sizeFeature = httpContext.Features.Get<IHttpMaxRequestBodySizeFeature>();
        if (sizeFeature is { IsReadOnly: false })
        {
            sizeFeature.MaxRequestBodySize = options.MaxBodyBytes;
        }

        if (httpContext.Request.ContentLength > options.MaxBodyBytes)
        {
            return PayloadTooLarge(options.MaxBodyBytes);
        }

        RawSyncBatchRequest? batch;
        try
        {
            batch = await httpContext.Request.ReadFromJsonAsync<RawSyncBatchRequest>(
                ApiJson.Options, cancellationToken);
        }
        catch (JsonException)
        {
            return ApiResults.InvalidRequest("Body không đúng định dạng JSON.");
        }

        if (batch?.Events is null || batch.Events.Count == 0)
        {
            return ApiResults.InvalidRequest("Batch phải có ít nhất một sự kiện.");
        }

        if (batch.Events.Count > options.MaxEventsPerBatch)
        {
            return ApiResults.Error(
                StatusCodes.Status400BadRequest,
                "too_many_events",
                $"Batch tối đa {options.MaxEventsPerBatch} sự kiện.");
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
        var results = new List<SyncEventResult>(batch.Events.Count);

        await using var transaction = await db.Database.BeginTransactionAsync(cancellationToken);
        try
        {
            foreach (var rawEvent in batch.Events)
            {
                if (rawEvent is null)
                {
                    results.Add(new SyncEventResult(
                        string.Empty,
                        SyncEventResultStatus.Rejected,
                        "invalid_event",
                        "Sự kiện không hợp lệ."));
                    continue;
                }

                // Parse the type out of the raw element so an unknown enum is rejected
                // per event instead of failing deserialization for the whole batch.
                if (!TryParseEventType(rawEvent.Type, out var eventType))
                {
                    results.Add(new SyncEventResult(
                        rawEvent.ClientEventId?.Trim() ?? string.Empty,
                        SyncEventResultStatus.Rejected,
                        "invalid_event",
                        "Loại sự kiện không hợp lệ."));
                    continue;
                }

                var dto = new SyncEventDto(
                    rawEvent.ClientEventId ?? string.Empty,
                    eventType,
                    rawEvent.OccurredAtUtc,
                    rawEvent.Payload);
                results.Add(await ProcessEventAsync(
                    db, family, deviceId, dto, now, options, cancellationToken));
            }

            await transaction.CommitAsync(cancellationToken);
        }
        catch (DbUpdateException)
        {
            await transaction.RollbackAsync(cancellationToken);
            return StoreUnavailable();
        }
        catch (NpgsqlException)
        {
            await transaction.RollbackAsync(cancellationToken);
            return StoreUnavailable();
        }

        return Results.Ok(new SyncBatchResponse(results, now));
    }

    private static async Task<SyncEventResult> ProcessEventAsync(
        CloudDbContext db,
        Family family,
        Guid deviceId,
        SyncEventDto dto,
        DateTimeOffset now,
        SyncOptions options,
        CancellationToken cancellationToken)
    {
        var validation = SyncPayloadValidator.Validate(dto, now);
        var clientEventId = dto.ClientEventId?.Trim() ?? string.Empty;
        var storableId = !string.IsNullOrWhiteSpace(clientEventId)
            && clientEventId.Length <= SyncPayloadValidator.MaxClientEventIdLength
            && !clientEventId.Any(char.IsControl);

        if (storableId && await db.SyncEvents
                .AsNoTracking()
                .AnyAsync(
                    e => e.FamilyId == family.Id && e.ClientEventId == clientEventId,
                    cancellationToken))
        {
            return new SyncEventResult(clientEventId, SyncEventResultStatus.Duplicate);
        }

        SyncEvent? entity = null;
        if (storableId && TryMapType(dto.Type, out var entityType))
        {
            entity = new SyncEvent
            {
                Id = Guid.NewGuid(),
                FamilyId = family.Id,
                ClientEventId = clientEventId,
                Type = entityType,
                ReceivedAtUtc = now,
                Status = SyncEventStatus.Pending,
            };
            db.SyncEvents.Add(entity);
        }

        if (!validation.IsValid)
        {
            await CompleteEventAsync(db, entity, SyncEventStatus.Rejected, now, cancellationToken);
            return new SyncEventResult(
                clientEventId, SyncEventResultStatus.Rejected, validation.Code, validation.Message);
        }

        var apply = await ApplyAsync(
            db, family, deviceId, validation.Value!, now, options, cancellationToken);
        if (apply.Code is not null)
        {
            await CompleteEventAsync(db, entity, SyncEventStatus.Rejected, now, cancellationToken);
            return new SyncEventResult(
                clientEventId, SyncEventResultStatus.Rejected, apply.Code, apply.Message);
        }

        // Applied events are fully handled here; alerts also queue a deduped notification for the push worker.
        if (validation.Value!.Type != ContractEventType.DailySummary)
        {
            await PlanAlertNotificationAsync(
                db, family.Id, validation.Value!, dto.OccurredAtUtc, now, cancellationToken);
        }

        await CompleteEventAsync(db, entity, SyncEventStatus.Processed, now, cancellationToken);
        return new SyncEventResult(clientEventId, SyncEventResultStatus.Applied);
    }

    private static async Task PlanAlertNotificationAsync(
        CloudDbContext db,
        Guid familyId,
        ValidatedSyncEvent validated,
        DateTimeOffset occurredAtUtc,
        DateTimeOffset now,
        CancellationToken cancellationToken)
    {
        var planned = NotificationPlanner.ForAlert(validated, occurredAtUtc);
        if (planned is null)
        {
            return;
        }

        var exists = db.Notifications.Local.Any(
                n => n.FamilyId == familyId && n.DedupeKey == planned.DedupeKey)
            || await db.Notifications
                .AsNoTracking()
                .AnyAsync(
                    n => n.FamilyId == familyId && n.DedupeKey == planned.DedupeKey,
                    cancellationToken);
        if (exists)
        {
            return;
        }

        db.Notifications.Add(new NotificationRecord
        {
            Id = Guid.NewGuid(),
            FamilyId = familyId,
            Category = planned.Category,
            DedupeKey = planned.DedupeKey,
            LocalDate = planned.LocalDate,
            CreatedAtUtc = now,
            Status = NotificationStatus.Pending,
        });
    }

    private static async Task CompleteEventAsync(
        CloudDbContext db,
        SyncEvent? entity,
        SyncEventStatus status,
        DateTimeOffset now,
        CancellationToken cancellationToken)
    {
        if (entity is not null)
        {
            entity.Status = status;
            entity.ProcessedAtUtc = now;
        }

        await db.SaveChangesAsync(cancellationToken);
    }

    private static async Task<ApplyResult> ApplyAsync(
        CloudDbContext db,
        Family family,
        Guid deviceId,
        ValidatedSyncEvent validated,
        DateTimeOffset now,
        SyncOptions options,
        CancellationToken cancellationToken) =>
        validated.Type == ContractEventType.DailySummary
            ? await ApplyDailySummaryAsync(
                db, family, deviceId, validated.DailySummary!, now, options, cancellationToken)
            : ApplyResult.Ok;

    private static async Task<ApplyResult> ApplyDailySummaryAsync(
        CloudDbContext db,
        Family family,
        Guid deviceId,
        DailySummaryPayload payload,
        DateTimeOffset now,
        SyncOptions options,
        CancellationToken cancellationToken)
    {
        var timeZoneId = payload.TimeZoneId ?? family.TimeZoneId;
        var timeZone = TimeZoneInfo.TryFindSystemTimeZoneById(timeZoneId, out var zone)
            ? zone
            : TimeZoneInfo.Utc;
        var todayLocal = DateOnly.FromDateTime(TimeZoneInfo.ConvertTime(now, timeZone).DateTime);
        var oldestLocalDate = todayLocal.AddDays(1 - options.DailySummaryWindowDays);

        if (payload.LocalDate < oldestLocalDate)
        {
            return ApplyResult.Rejected("stale_summary", "Bản tóm tắt đã quá cũ.");
        }

        if (payload.LocalDate > todayLocal)
        {
            return ApplyResult.Rejected("invalid_payload", "Ngày tóm tắt không hợp lệ.");
        }

        if (payload.TimeZoneId is { } newTimeZoneId && newTimeZoneId != family.TimeZoneId)
        {
            var previousTimeZoneId = family.TimeZoneId;
            family.TimeZoneId = newTimeZoneId;

            var preferences = await db.NotificationPreferences
                .FirstOrDefaultAsync(p => p.FamilyId == family.Id, cancellationToken);
            if (preferences is not null)
            {
                preferences.TimeZoneId = newTimeZoneId;
                preferences.UpdatedAtUtc = now;
            }

            db.CloudAuditLogs.Add(CloudAudit.Create(
                family.Id,
                deviceId,
                "FamilyTimeZoneChanged",
                now,
                new { oldTimeZoneId = previousTimeZoneId, newTimeZoneId }));
        }

        var summary = await db.DailySummaries
            .FirstOrDefaultAsync(
                s => s.FamilyId == family.Id
                    && s.DesktopDeviceId == deviceId
                    && s.LocalDate == payload.LocalDate,
                cancellationToken);

        if (summary is not null && summary.UpdatedAtUtc >= payload.GeneratedAtUtc)
        {
            return ApplyResult.Ok;
        }

        if (summary is null)
        {
            summary = new DailySummary
            {
                Id = Guid.NewGuid(),
                FamilyId = family.Id,
                DesktopDeviceId = deviceId,
                LocalDate = payload.LocalDate,
            };
            db.DailySummaries.Add(summary);
        }

        summary.SessionsCount = payload.SessionsCount;
        summary.ActiveSeconds = payload.ActiveSeconds;
        summary.StudySeconds = payload.StudySeconds;
        summary.IdleSeconds = payload.IdleSeconds;
        summary.OnlineClassSeconds = payload.OnlineClassSeconds;
        summary.MonitoringEligibleSeconds = payload.MonitoringEligibleSeconds;
        summary.MonitoredSeconds = payload.MonitoredSeconds;
        summary.FocusedSeconds = payload.FocusedSeconds;
        summary.DistractedSeconds = payload.DistractedSeconds;
        summary.AbsentSeconds = payload.AbsentSeconds;
        summary.DrowsySeconds = payload.DrowsySeconds;
        summary.BlockedAttempts = payload.BlockedAttempts;
        summary.AttentionPercent = payload.AttentionPercent;
        summary.CoveragePercent = payload.CoveragePercent;
        summary.UpdatedAtUtc = payload.GeneratedAtUtc;
        return ApplyResult.Ok;
    }

    private static bool TryParseEventType(JsonElement element, out ContractEventType type)
    {
        type = default;
        if (element.ValueKind is not (JsonValueKind.String or JsonValueKind.Number))
        {
            return false;
        }

        try
        {
            type = element.Deserialize<ContractEventType>(ApiJson.Options);
        }
        catch (JsonException)
        {
            return false;
        }

        return Enum.IsDefined(type);
    }

    private static bool TryMapType(ContractEventType type, out EntityEventType mapped)
    {
        switch (type)
        {
            case ContractEventType.DailySummary:
                mapped = EntityEventType.DailySummary;
                return true;
            case ContractEventType.AttentionAlert:
                mapped = EntityEventType.AttentionAlert;
                return true;
            case ContractEventType.ProtectionOff:
                mapped = EntityEventType.ProtectionOff;
                return true;
            case ContractEventType.SecurityAlert:
                mapped = EntityEventType.SecurityAlert;
                return true;
            default:
                mapped = default;
                return false;
        }
    }

    private static IResult PayloadTooLarge(int maxBodyBytes) =>
        ApiResults.Error(
            StatusCodes.Status413PayloadTooLarge,
            "payload_too_large",
            $"Batch vượt quá giới hạn {maxBodyBytes / 1024} KB.");

    private static IResult StoreUnavailable() =>
        ApiResults.Error(
            StatusCodes.Status503ServiceUnavailable,
            "store_unavailable",
            "Không lưu được sự kiện, vui lòng thử lại.");

    private readonly record struct ApplyResult(string? Code, string? Message)
    {
        public static ApplyResult Ok => new(null, null);

        public static ApplyResult Rejected(string code, string message) => new(code, message);
    }
}

internal sealed record RawSyncBatchRequest(IReadOnlyList<RawSyncEventDto?>? Events);

internal sealed record RawSyncEventDto(
    string? ClientEventId,
    JsonElement Type,
    DateTimeOffset OccurredAtUtc,
    JsonElement Payload);
