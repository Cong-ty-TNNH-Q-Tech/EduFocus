namespace DontBeLazy.Ports.Outbound.Services;

public interface INotificationPort
{
    void ShowNotification(string title, string message);
}
