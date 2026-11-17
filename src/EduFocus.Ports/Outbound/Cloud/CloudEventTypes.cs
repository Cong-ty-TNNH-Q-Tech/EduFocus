namespace EduFocus.Ports.Outbound.Cloud;

/// <summary>
/// Cloud event type slugs shared with the cloud contracts. Only these aggregate
/// event types may ever leave the device.
/// </summary>
public static class CloudEventTypes
{
    public const string DailySummary = "daily_summary";
    public const string AttentionAlert = "attention_alert";
    public const string ProtectionOff = "protection_off";
    public const string SecurityAlert = "security_alert";

    public static bool IsKnown(string? type) =>
        type is DailySummary or AttentionAlert or ProtectionOff or SecurityAlert;
}
