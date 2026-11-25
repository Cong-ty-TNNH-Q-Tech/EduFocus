using System.Text.Json;
using EduFocus.Parent.Core.Push;
using EduFocus.Parent.Core.Settings;

namespace EduFocus.Parent.App.Services;

/// <summary>
/// Entry point for decrypted UnifiedPush messages. Messages are parsed and
/// deduplicated before display; unknown types and repeated notification ids
/// are dropped silently.
/// </summary>
public sealed class PushMessageHandler(
    PushNotificationDedupe dedupe,
    PushNotificationPresenter presenter,
    ParentSettingsStore settings)
{
    public void Handle(byte[] content)
    {
        if (settings.DeviceId is null || content.Length == 0)
            return;

        Dictionary<string, string>? data;
        try
        {
            data = JsonSerializer.Deserialize<Dictionary<string, string>>(content);
        }
        catch (JsonException)
        {
            return;
        }

        if (data is null
            || !PushNotificationPayloadParser.TryParse(data, out var payload)
            || payload is null)
        {
            return;
        }

        if (!dedupe.TryRemember(payload.DedupeKey))
            return;

        presenter.Present(payload, AppLifecycleState.IsForeground);
    }
}
