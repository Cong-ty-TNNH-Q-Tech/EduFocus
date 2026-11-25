using EduFocus.Cloud.Contracts.Notifications;

namespace EduFocus.Parent.Core.Presentation;

public sealed class NotificationItem
{
    public NotificationItem(NotificationDto notification, TimeZoneInfo timeZone)
    {
        Id = notification.Id;
        CategoryText = notification.Category switch
        {
            NotificationKind.DailySummary => "Tổng kết ngày",
            NotificationKind.AttentionAlert => "Cảnh báo tập trung",
            NotificationKind.ProtectionOff => "Bảo vệ tạm tắt",
            NotificationKind.SecurityAlert => "Cảnh báo bảo mật",
            NotificationKind.BlockedAttempt => "Truy cập bị chặn",
            _ => "Thông báo",
        };
        TimeText = VietnameseFormat.DateTimeLocal(notification.CreatedAtUtc, timeZone);
        StatusText = notification.Status switch
        {
            NotificationDeliveryStatus.Pending => "Đang chờ gửi",
            NotificationDeliveryStatus.Sent => "Đã gửi",
            NotificationDeliveryStatus.Suppressed => "Bị tạm hoãn",
            NotificationDeliveryStatus.Failed => "Gửi lỗi",
            _ => string.Empty,
        };
    }

    public Guid Id { get; }

    public string CategoryText { get; }

    public string TimeText { get; }

    public string StatusText { get; }
}
