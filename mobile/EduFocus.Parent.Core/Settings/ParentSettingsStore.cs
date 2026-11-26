using EduFocus.Parent.Core.Push;

namespace EduFocus.Parent.Core.Settings;

/// <summary>Non-secret key/value storage (MAUI Preferences on the app).</summary>
public interface IKeyValueStore
{
    string? Get(string key);

    void Set(string key, string value);

    void Remove(string key);
}

/// <summary>
/// Persists everything about a link except the token: the server URL doubles as
/// the self-host configuration and survives unlink so re-pairing is quick.
/// </summary>
public sealed class ParentSettingsStore(IKeyValueStore store)
{
    public const string CloudBaseUrlKey = "edufocus.parent.cloud_base_url";
    public const string FamilyIdKey = "edufocus.parent.family_id";
    public const string DeviceIdKey = "edufocus.parent.device_id";
    public const string DesktopNameKey = "edufocus.parent.desktop_name";
    public const string TimeZoneIdKey = "edufocus.parent.time_zone";
    public const string PushEndpointKey = "edufocus.parent.push_endpoint";
    public const string PushP256dhKey = "edufocus.parent.push_p256dh";
    public const string PushAuthKey = "edufocus.parent.push_auth";
    public const string PushVapidPublicKeyKey = "edufocus.parent.push_vapid_public";
    public const string PushAppVersionKey = "edufocus.parent.push_app_version";
    public const string PushWebSocketEnabledKey = "edufocus.parent.push_websocket";
    private const string LegacyPushTokenKey = "edufocus.parent.push_token";

    public string? CloudBaseUrl
    {
        get => store.Get(CloudBaseUrlKey);
        set => SetOptional(CloudBaseUrlKey, value?.Trim().TrimEnd('/'));
    }

    public Guid? FamilyId
    {
        get => GetGuid(FamilyIdKey);
        set => SetOptional(FamilyIdKey, value?.ToString("D"));
    }

    public Guid? DeviceId
    {
        get => GetGuid(DeviceIdKey);
        set => SetOptional(DeviceIdKey, value?.ToString("D"));
    }

    public string? DesktopName
    {
        get => store.Get(DesktopNameKey);
        set => SetOptional(DesktopNameKey, value?.Trim());
    }

    public string? TimeZoneId
    {
        get => store.Get(TimeZoneIdKey);
        set => SetOptional(TimeZoneIdKey, value?.Trim());
    }

    public string? PushEndpoint
    {
        get => store.Get(PushEndpointKey);
        set => SetOptional(PushEndpointKey, value?.Trim());
    }

    public string? PushP256dh
    {
        get => store.Get(PushP256dhKey);
        set => SetOptional(PushP256dhKey, value?.Trim());
    }

    public string? PushAuth
    {
        get => store.Get(PushAuthKey);
        set => SetOptional(PushAuthKey, value?.Trim());
    }

    public string? PushVapidPublicKey
    {
        get => store.Get(PushVapidPublicKeyKey);
        set => SetOptional(PushVapidPublicKeyKey, value?.Trim());
    }

    public string? PushAppVersion
    {
        get => store.Get(PushAppVersionKey);
        set => SetOptional(PushAppVersionKey, value?.Trim());
    }

    public bool PushWebSocketEnabled
    {
        get => string.Equals(
            store.Get(PushWebSocketEnabledKey),
            "true",
            StringComparison.OrdinalIgnoreCase);
        set => SetOptional(PushWebSocketEnabledKey, value ? "true" : null);
    }

    public PushEndpointRegistration? GetPushEndpoint()
    {
        var endpoint = PushEndpoint;
        var p256dh = PushP256dh;
        var auth = PushAuth;
        return string.IsNullOrWhiteSpace(endpoint)
            || string.IsNullOrWhiteSpace(p256dh)
            || string.IsNullOrWhiteSpace(auth)
            ? null
            : new PushEndpointRegistration(endpoint, p256dh, auth);
    }

    public void SetPushEndpoint(PushEndpointRegistration endpoint)
    {
        PushEndpoint = endpoint.Endpoint;
        PushP256dh = endpoint.P256dh;
        PushAuth = endpoint.Auth;
    }

    public void ClearPushEndpoint()
    {
        store.Remove(PushEndpointKey);
        store.Remove(PushP256dhKey);
        store.Remove(PushAuthKey);
    }

    public void RemoveLegacyPushToken() => store.Remove(LegacyPushTokenKey);

    /// <summary>
    /// Forgets the link identifiers and the UnifiedPush endpoint but keeps the
    /// server URL so re-pairing is quick and an old endpoint is never reused.
    /// </summary>
    public void ClearLink()
    {
        store.Remove(FamilyIdKey);
        store.Remove(DeviceIdKey);
        store.Remove(DesktopNameKey);
        store.Remove(TimeZoneIdKey);
        ClearPushEndpoint();
        store.Remove(PushVapidPublicKeyKey);
        store.Remove(PushAppVersionKey);
        store.Remove(PushWebSocketEnabledKey);
        store.Remove(LegacyPushTokenKey);
    }

    private Guid? GetGuid(string key) =>
        Guid.TryParse(store.Get(key), out var value) ? value : null;

    private void SetOptional(string key, string? value)
    {
        if (string.IsNullOrWhiteSpace(value))
            store.Remove(key);
        else
            store.Set(key, value);
    }
}
