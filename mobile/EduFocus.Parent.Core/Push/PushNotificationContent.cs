namespace EduFocus.Parent.Core.Push;

public sealed record PushNotificationContent(string Title, string Body);

/// <summary>
/// Generic wording per backend type. Notification text never contains student
/// names, app names, domains or URLs; details stay behind the app.
/// </summary>
public static class PushNotificationContentFactory
{
    public static PushNotificationContent Create(PushNotificationType type) => type switch
    {
        PushNotificationType.DailySummary => new PushNotificationContent(
            "Báo cáo ngày",
            "Báo cáo tổng hợp hoạt động học tập đã sẵn sàng. Mở ứng dụng để xem chi tiết."),
        PushNotificationType.AttentionAlert => new PushNotificationContent(
            "Cảnh báo tập trung",
            "Có cảnh báo mới về mức độ tập trung trong buổi học. Mở ứng dụng để xem báo cáo."),
        PushNotificationType.ProtectionOff => new PushNotificationContent(
            "Bảo vệ tạm tắt",
            "Tính năng bảo vệ trên máy tính đang tạm tắt. Mở ứng dụng để kiểm tra."),
        PushNotificationType.SecurityAlert => new PushNotificationContent(
            "Cảnh báo bảo mật",
            "Có cảnh báo bảo mật mới. Mở ứng dụng để xem chi tiết."),
        _ => throw new ArgumentOutOfRangeException(nameof(type)),
    };
}
