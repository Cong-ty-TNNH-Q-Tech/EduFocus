namespace EduFocus.Parent.App.Services;

/// <summary>
/// Receives <c>edufocus://pair?...</c> intents from Android. The payload is only
/// prefilled on the pairing screen; claiming always needs an explicit tap so a
/// malicious link cannot silently re-link the phone.
/// </summary>
public static class PairingDeepLink
{
    public const string Prefix = "edufocus://pair";

    private static string? _pending;

    public static event Action<string>? Received;

    public static void Handle(string? uri)
    {
        if (string.IsNullOrWhiteSpace(uri)
            || !uri.StartsWith(Prefix, StringComparison.OrdinalIgnoreCase))
        {
            return;
        }

        _pending = uri;
        Received?.Invoke(uri);
    }

    public static string? ConsumePending()
    {
        var pending = _pending;
        _pending = null;
        return pending;
    }
}
