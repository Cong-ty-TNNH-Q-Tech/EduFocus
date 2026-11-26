using System.Text.Json;
using System.Text.Json.Serialization;
using EduFocus.Cloud.Contracts.Json;
using EduFocus.Cloud.Contracts.Sync;

namespace EduFocus.Cloud.Api.Sync;

public sealed record ValidatedSyncEvent(
    SyncEventType Type,
    DailySummaryPayload? DailySummary = null,
    AttentionAlertPayload? AttentionAlert = null,
    ProtectionOffPayload? ProtectionOff = null,
    SecurityAlertPayload? SecurityAlert = null,
    BlockedAttemptPayload? BlockedAttempt = null);

public readonly record struct SyncValidationResult(
    ValidatedSyncEvent? Value,
    string? Code,
    string? Message)
{
    public bool IsValid => Value is not null;

    public static SyncValidationResult Valid(ValidatedSyncEvent value) => new(value, null, null);

    public static SyncValidationResult Invalid(string code, string message) => new(null, code, message);
}

public static class SyncPayloadValidator
{
    public const int MaxClientEventIdLength = 64;
    public const int MinAttentionDurationSeconds = 60;
    public const int AlertLocalDatePastDays = 7;
    private const int MaxDailySeconds = 86400;
    private static readonly TimeSpan MaxFutureSkew = TimeSpan.FromMinutes(5);

    private static readonly JsonSerializerOptions StrictOptions = new(ApiJson.Options)
    {
        UnmappedMemberHandling = JsonUnmappedMemberHandling.Disallow,
    };

    private static readonly Dictionary<SyncEventType, string[]> AllowedFields = new()
    {
        [SyncEventType.DailySummary] =
        [
            "localDate", "timeZoneId", "generatedAtUtc", "sessionsCount", "activeSeconds",
            "studySeconds", "idleSeconds", "onlineClassSeconds", "monitoringEligibleSeconds",
            "monitoredSeconds", "focusedSeconds", "distractedSeconds", "absentSeconds",
            "drowsySeconds", "blockedAttempts", "attentionPercent", "coveragePercent",
        ],
        [SyncEventType.AttentionAlert] = ["kind", "localDate", "startedAtUtc", "durationSeconds"],
        [SyncEventType.ProtectionOff] = ["source", "localDate"],
        [SyncEventType.SecurityAlert] = ["kind", "localDate"],
        [SyncEventType.BlockedAttempt] = ["localDate"],
    };

    private static readonly Dictionary<SyncEventType, string[]> RequiredFields = new()
    {
        [SyncEventType.DailySummary] = ["localDate", "generatedAtUtc"],
        [SyncEventType.AttentionAlert] = ["kind", "localDate", "startedAtUtc", "durationSeconds"],
        [SyncEventType.ProtectionOff] = ["source", "localDate"],
        [SyncEventType.SecurityAlert] = ["kind", "localDate"],
        [SyncEventType.BlockedAttempt] = ["localDate"],
    };

    public static SyncValidationResult Validate(SyncEventDto? dto, DateTimeOffset? nowUtc = null)
    {
        var now = nowUtc ?? DateTimeOffset.UtcNow;

        if (dto is null)
        {
            return SyncValidationResult.Invalid("invalid_event", "Sự kiện không hợp lệ.");
        }

        var clientEventId = dto.ClientEventId?.Trim();
        if (string.IsNullOrWhiteSpace(clientEventId)
            || clientEventId.Length > MaxClientEventIdLength
            || clientEventId.Any(char.IsControl))
        {
            return SyncValidationResult.Invalid("invalid_event", "Mã sự kiện không hợp lệ.");
        }

        var minOccurredAt = dto.Type == SyncEventType.DailySummary
            ? now.AddDays(-32)
            : now.AddDays(-(AlertLocalDatePastDays + 1));
        if (dto.OccurredAtUtc < minOccurredAt || dto.OccurredAtUtc > now + MaxFutureSkew)
        {
            return SyncValidationResult.Invalid("invalid_event", "Thời điểm sự kiện không hợp lệ.");
        }

        if (!Enum.IsDefined(dto.Type) || !AllowedFields.TryGetValue(dto.Type, out var allowedFields))
        {
            return SyncValidationResult.Invalid("invalid_event", "Loại sự kiện không hợp lệ.");
        }

        if (dto.Payload.ValueKind != JsonValueKind.Object)
        {
            return SyncValidationResult.Invalid("invalid_payload", "Payload phải là một object.");
        }

        foreach (var property in dto.Payload.EnumerateObject())
        {
            if (!allowedFields.Contains(property.Name, StringComparer.Ordinal))
            {
                return SyncValidationResult.Invalid(
                    "unknown_field",
                    $"Payload chứa trường không được phép: {property.Name}.");
            }
        }

        foreach (var required in RequiredFields[dto.Type])
        {
            if (!dto.Payload.TryGetProperty(required, out _))
            {
                return SyncValidationResult.Invalid(
                    "invalid_payload",
                    $"Payload thiếu trường bắt buộc: {required}.");
            }
        }

        try
        {
            return dto.Type switch
            {
                SyncEventType.DailySummary => ValidateDailySummary(dto.Payload, now),
                SyncEventType.AttentionAlert => ValidateAttentionAlert(dto.Payload, now),
                SyncEventType.ProtectionOff => ValidateProtectionOff(dto.Payload, now),
                SyncEventType.SecurityAlert => ValidateSecurityAlert(dto.Payload, now),
                SyncEventType.BlockedAttempt => ValidateBlockedAttempt(dto.Payload, now),
                _ => SyncValidationResult.Invalid("invalid_event", "Loại sự kiện không hợp lệ."),
            };
        }
        catch (JsonException)
        {
            return SyncValidationResult.Invalid("invalid_payload", "Payload không đúng định dạng.");
        }
    }

    private static SyncValidationResult ValidateDailySummary(JsonElement payload, DateTimeOffset now)
    {
        var summary = payload.Deserialize<DailySummaryPayload>(StrictOptions);
        if (summary is null)
        {
            return SyncValidationResult.Invalid("invalid_payload", "Payload không đúng định dạng.");
        }

        if (summary.GeneratedAtUtc < now.AddDays(-32) || summary.GeneratedAtUtc > now + MaxFutureSkew)
        {
            return SyncValidationResult.Invalid("invalid_payload", "generatedAtUtc không hợp lệ.");
        }

        if (summary.SessionsCount < 0 || summary.BlockedAttempts < 0)
        {
            return SyncValidationResult.Invalid("invalid_payload", "Bộ đếm không được âm.");
        }

        long[] seconds =
        [
            summary.ActiveSeconds, summary.StudySeconds, summary.IdleSeconds,
            summary.OnlineClassSeconds, summary.MonitoringEligibleSeconds, summary.MonitoredSeconds,
            summary.FocusedSeconds, summary.DistractedSeconds, summary.AbsentSeconds,
            summary.DrowsySeconds,
        ];
        if (seconds.Any(value => value < 0 || value > MaxDailySeconds))
        {
            return SyncValidationResult.Invalid(
                "invalid_payload",
                "Số giây phải nằm trong khoảng 0–86400.");
        }

        if (IsInvalidPercent(summary.AttentionPercent) || IsInvalidPercent(summary.CoveragePercent))
        {
            return SyncValidationResult.Invalid(
                "invalid_payload",
                "Phần trăm phải nằm trong khoảng 0–100 hoặc null.");
        }

        if (summary.TimeZoneId is { } timeZoneId)
        {
            if (timeZoneId.Length > 64 || !TimeZoneInfo.TryFindSystemTimeZoneById(timeZoneId, out _))
            {
                return SyncValidationResult.Invalid(
                    "invalid_time_zone",
                    "Múi giờ không hợp lệ.");
            }
        }

        return SyncValidationResult.Valid(new ValidatedSyncEvent(
            SyncEventType.DailySummary,
            DailySummary: summary));
    }

    private static SyncValidationResult ValidateAttentionAlert(JsonElement payload, DateTimeOffset now)
    {
        var alert = payload.Deserialize<AttentionAlertPayload>(StrictOptions);
        if (alert is null)
        {
            return SyncValidationResult.Invalid("invalid_payload", "Payload không đúng định dạng.");
        }

        if (!Enum.IsDefined(alert.Kind))
        {
            return SyncValidationResult.Invalid("invalid_payload", "Loại cảnh báo không hợp lệ.");
        }

        if (!IsLocalDateInRange(alert.LocalDate, now))
        {
            return SyncValidationResult.Invalid("invalid_payload", "localDate nằm ngoài khoảng hợp lệ.");
        }

        if (alert.StartedAtUtc < now.AddDays(-(AlertLocalDatePastDays + 1)) || alert.StartedAtUtc > now + MaxFutureSkew)
        {
            return SyncValidationResult.Invalid("invalid_payload", "startedAtUtc không hợp lệ.");
        }

        if (alert.DurationSeconds < MinAttentionDurationSeconds)
        {
            return SyncValidationResult.Invalid(
                "invalid_payload",
                "Cảnh báo chú ý phải kéo dài ít nhất 60 giây.");
        }

        if (alert.DurationSeconds > MaxDailySeconds)
        {
            return SyncValidationResult.Invalid("invalid_payload", "Thời lượng không hợp lệ.");
        }

        return SyncValidationResult.Valid(new ValidatedSyncEvent(
            SyncEventType.AttentionAlert,
            AttentionAlert: alert));
    }

    private static SyncValidationResult ValidateProtectionOff(JsonElement payload, DateTimeOffset now)
    {
        var value = payload.Deserialize<ProtectionOffPayload>(StrictOptions);
        if (value is null || !Enum.IsDefined(value.Source))
        {
            return SyncValidationResult.Invalid("invalid_payload", "Nguồn tắt bảo vệ không hợp lệ.");
        }

        if (!IsLocalDateInRange(value.LocalDate, now))
        {
            return SyncValidationResult.Invalid("invalid_payload", "localDate nằm ngoài khoảng hợp lệ.");
        }

        return SyncValidationResult.Valid(new ValidatedSyncEvent(
            SyncEventType.ProtectionOff,
            ProtectionOff: value));
    }

    private static SyncValidationResult ValidateSecurityAlert(JsonElement payload, DateTimeOffset now)
    {
        var value = payload.Deserialize<SecurityAlertPayload>(StrictOptions);
        if (value is null || !Enum.IsDefined(value.Kind))
        {
            return SyncValidationResult.Invalid("invalid_payload", "Loại cảnh báo an ninh không hợp lệ.");
        }

        if (!IsLocalDateInRange(value.LocalDate, now))
        {
            return SyncValidationResult.Invalid("invalid_payload", "localDate nằm ngoài khoảng hợp lệ.");
        }

        return SyncValidationResult.Valid(new ValidatedSyncEvent(
            SyncEventType.SecurityAlert,
            SecurityAlert: value));
    }

    private static SyncValidationResult ValidateBlockedAttempt(JsonElement payload, DateTimeOffset now)
    {
        var value = payload.Deserialize<BlockedAttemptPayload>(StrictOptions);
        if (value is null)
        {
            return SyncValidationResult.Invalid("invalid_payload", "Payload không đúng định dạng.");
        }

        if (!IsLocalDateInRange(value.LocalDate, now))
        {
            return SyncValidationResult.Invalid("invalid_payload", "localDate nằm ngoài khoảng hợp lệ.");
        }

        return SyncValidationResult.Valid(new ValidatedSyncEvent(
            SyncEventType.BlockedAttempt,
            BlockedAttempt: value));
    }

    private static bool IsLocalDateInRange(DateOnly localDate, DateTimeOffset now)
    {
        var utcToday = DateOnly.FromDateTime(now.UtcDateTime);
        return localDate >= utcToday.AddDays(-AlertLocalDatePastDays)
            && localDate <= utcToday.AddDays(1);
    }

    private static bool IsInvalidPercent(double? value) => value is < 0 or > 100;
}
