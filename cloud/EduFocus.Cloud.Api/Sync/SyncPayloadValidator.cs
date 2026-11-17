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
    SecurityAlertPayload? SecurityAlert = null);

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
    private const int MaxDailySeconds = 86400;

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
    };

    private static readonly Dictionary<SyncEventType, string[]> RequiredFields = new()
    {
        [SyncEventType.DailySummary] = ["localDate", "generatedAtUtc"],
        [SyncEventType.AttentionAlert] = ["kind", "localDate", "startedAtUtc", "durationSeconds"],
        [SyncEventType.ProtectionOff] = ["source", "localDate"],
        [SyncEventType.SecurityAlert] = ["kind", "localDate"],
    };

    public static SyncValidationResult Validate(SyncEventDto? dto)
    {
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

        if (dto.OccurredAtUtc.Year < 2000)
        {
            return SyncValidationResult.Invalid("invalid_event", "Thời điểm sự kiện không hợp lệ.");
        }

        if (!AllowedFields.TryGetValue(dto.Type, out var allowedFields))
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
                SyncEventType.DailySummary => ValidateDailySummary(dto.Payload),
                SyncEventType.AttentionAlert => ValidateAttentionAlert(dto.Payload),
                SyncEventType.ProtectionOff => SyncValidationResult.Valid(new ValidatedSyncEvent(
                    dto.Type,
                    ProtectionOff: dto.Payload.Deserialize<ProtectionOffPayload>(StrictOptions))),
                SyncEventType.SecurityAlert => SyncValidationResult.Valid(new ValidatedSyncEvent(
                    dto.Type,
                    SecurityAlert: dto.Payload.Deserialize<SecurityAlertPayload>(StrictOptions))),
                _ => SyncValidationResult.Invalid("invalid_event", "Loại sự kiện không hợp lệ."),
            };
        }
        catch (JsonException)
        {
            return SyncValidationResult.Invalid("invalid_payload", "Payload không đúng định dạng.");
        }
    }

    private static SyncValidationResult ValidateDailySummary(JsonElement payload)
    {
        var summary = payload.Deserialize<DailySummaryPayload>(StrictOptions);
        if (summary is null)
        {
            return SyncValidationResult.Invalid("invalid_payload", "Payload không đúng định dạng.");
        }

        if (summary.GeneratedAtUtc.Year < 2000)
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

    private static SyncValidationResult ValidateAttentionAlert(JsonElement payload)
    {
        var alert = payload.Deserialize<AttentionAlertPayload>(StrictOptions);
        if (alert is null)
        {
            return SyncValidationResult.Invalid("invalid_payload", "Payload không đúng định dạng.");
        }

        if (alert.StartedAtUtc.Year < 2000)
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

    private static bool IsInvalidPercent(double? value) => value is < 0 or > 100;
}
