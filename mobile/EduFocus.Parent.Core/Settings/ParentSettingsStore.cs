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

    /// <summary>Forgets the link identifiers but keeps the server URL.</summary>
    public void ClearLink()
    {
        store.Remove(FamilyIdKey);
        store.Remove(DeviceIdKey);
        store.Remove(DesktopNameKey);
        store.Remove(TimeZoneIdKey);
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
