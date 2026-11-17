using EduFocus.Parent.Core.Push;

namespace EduFocus.Parent.App.Services;

/// <summary>
/// Receives the notification tap intent from MainActivity and hands the target
/// route to AppShell. A cold start stores the route until the shell is ready.
/// </summary>
public static class PushTapRouter
{
    public const string TypeExtra = "edufocus.push.type";
    public const string DateExtra = "edufocus.push.date";

    private static string? _pending;

    public static event Action<string>? Tapped;

    public static void Handle(string? typeKey, string? localDate)
    {
        if (!PushTapRoute.TryCreate(typeKey, localDate, out var route) || route is null)
            return;

        if (Tapped is null)
        {
            _pending = route;
            return;
        }

        MainThread.BeginInvokeOnMainThread(() => Tapped?.Invoke(route));
    }

    public static string? ConsumePending()
    {
        var route = _pending;
        _pending = null;
        return route;
    }
}
