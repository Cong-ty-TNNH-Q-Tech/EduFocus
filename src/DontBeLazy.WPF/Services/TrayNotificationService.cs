using DontBeLazy.Ports.Outbound.Services;

namespace DontBeLazy.WPF.Services;

public sealed class TrayNotificationService : INotificationPort
{
    public event Action<string, string>? NotificationRequested;

    public void ShowNotification(string title, string message)
    {
        NotificationRequested?.Invoke(title, message);
    }
}
