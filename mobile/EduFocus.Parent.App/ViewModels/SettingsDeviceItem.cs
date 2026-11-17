using EduFocus.Cloud.Contracts.Devices;
using EduFocus.Parent.Core.Presentation;

namespace EduFocus.Parent.App.ViewModels;

public sealed class SettingsDeviceItem
{
    public SettingsDeviceItem(DeviceDto device, Guid? currentDeviceId, TimeZoneInfo timeZone)
    {
        Id = device.Id;
        IsCurrent = currentDeviceId == device.Id;
        Name = IsCurrent ? $"{device.Name} (máy này)" : device.Name;
        var kind = string.Equals(device.Kind, "Desktop", StringComparison.OrdinalIgnoreCase)
            ? "Máy tính"
            : "Điện thoại";
        MetaText = $"{kind} · {device.Platform}";

        if (device.RevokedAtUtc is { } revokedAt)
            LastSeenText = $"Đã thu hồi lúc {VietnameseFormat.DateTimeLocal(revokedAt, timeZone)}";
        else if (device.LastSeenAtUtc is { } lastSeen)
            LastSeenText = $"Hoạt động lúc {VietnameseFormat.DateTimeLocal(lastSeen, timeZone)}";
        else
            LastSeenText = "Chưa hoạt động";

        CanRevoke = !IsCurrent && device.RevokedAtUtc is null;
    }

    public Guid Id { get; }

    public string Name { get; }

    public string MetaText { get; }

    public string LastSeenText { get; }

    public bool IsCurrent { get; }

    public bool CanRevoke { get; }
}
