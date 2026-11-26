namespace EduFocus.Cloud.Contracts.Push;

public sealed record RegisterPushRequest(
    string Endpoint,
    string P256dh,
    string Auth,
    string Platform,
    string? AppVersion = null);

public sealed record RegisterPushResponse(DateTimeOffset UpdatedAtUtc);

public sealed record PushConfigResponse(
    bool Enabled,
    string? VapidPublicKey,
    bool WebSocketEnabled = false);
