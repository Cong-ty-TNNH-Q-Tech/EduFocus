namespace EduFocus.Cloud.Contracts.Push;

public sealed record RegisterPushRequest(
    string FcmToken,
    string Platform,
    string? AppVersion = null);

public sealed record RegisterPushResponse(DateTimeOffset UpdatedAtUtc);
