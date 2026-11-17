using EduFocus.Parent.Core.Settings;

namespace EduFocus.Parent.App.Services;

public sealed class PreferencesKeyValueStore : IKeyValueStore
{
    public string? Get(string key) =>
        Preferences.Default.ContainsKey(key) ? Preferences.Default.Get<string>(key, string.Empty) : null;

    public void Set(string key, string value) => Preferences.Default.Set(key, value);

    public void Remove(string key) => Preferences.Default.Remove(key);
}
