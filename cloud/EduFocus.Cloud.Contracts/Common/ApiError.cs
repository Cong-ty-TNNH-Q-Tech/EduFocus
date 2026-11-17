namespace EduFocus.Cloud.Contracts.Common;

public sealed record ApiError(
    string Code,
    string Message,
    string? TraceId = null,
    IReadOnlyDictionary<string, string[]>? Details = null);

public sealed record ApiErrorResponse(ApiError Error);

public sealed record HealthResponse(string Status, DateTimeOffset CheckedAtUtc);

public sealed record OperationResponse(bool Success);
