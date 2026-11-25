using System.Globalization;
using EduFocus.Parent.Core.Settings;

namespace EduFocus.Parent.Core.Push;

/// <summary>
/// Remembers recently shown notification ids so a repeated Web Push delivery does not
/// create a second notification. Entries persist in preferences as
/// <c>key|unix-ms</c> lines, capped and pruned after seven days.
/// </summary>
public sealed class PushNotificationDedupe(IKeyValueStore store, Func<DateTimeOffset>? clock = null)
{
    public const string StorageKey = "edufocus.parent.push_seen";
    private const int MaxEntries = 64;
    private static readonly TimeSpan Ttl = TimeSpan.FromDays(7);

    public bool TryRemember(string dedupeKey)
    {
        if (string.IsNullOrWhiteSpace(dedupeKey))
            return true;

        var now = (clock?.Invoke() ?? DateTimeOffset.UtcNow).ToUnixTimeMilliseconds();
        var entries = Load();
        var cutoff = now - (long)Ttl.TotalMilliseconds;
        entries.RemoveAll(entry => entry.SeenAt < cutoff);

        if (entries.Exists(entry => string.Equals(entry.Key, dedupeKey, StringComparison.Ordinal)))
        {
            Save(entries);
            return false;
        }

        entries.Add(new SeenEntry(dedupeKey, now));
        if (entries.Count > MaxEntries)
            entries.RemoveRange(0, entries.Count - MaxEntries);

        Save(entries);
        return true;
    }

    public void Clear() => store.Remove(StorageKey);

    private List<SeenEntry> Load()
    {
        var entries = new List<SeenEntry>();
        var raw = store.Get(StorageKey);
        if (string.IsNullOrWhiteSpace(raw))
            return entries;

        foreach (var line in raw.Split('\n', StringSplitOptions.RemoveEmptyEntries))
        {
            var separator = line.LastIndexOf('|');
            if (separator <= 0 || separator == line.Length - 1)
                continue;

            var key = line[..separator];
            if (long.TryParse(
                    line[(separator + 1)..],
                    NumberStyles.Integer,
                    CultureInfo.InvariantCulture,
                    out var seenAt))
            {
                entries.Add(new SeenEntry(key, seenAt));
            }
        }

        return entries;
    }

    private void Save(List<SeenEntry> entries) =>
        store.Set(
            StorageKey,
            string.Join(
                '\n',
                entries.Select(entry =>
                    entry.Key + "|" + entry.SeenAt.ToString(CultureInfo.InvariantCulture))));

    private sealed record SeenEntry(string Key, long SeenAt);
}
