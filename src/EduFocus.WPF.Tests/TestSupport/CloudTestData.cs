using EduFocus.Ports.DTOs;

namespace EduFocus.WPF.Tests.TestSupport;

internal static class CloudTestData
{
    public const string BaseUrl = "https://cloud.example.test";

    public static CloudLinkStatusSnapshot Empty() =>
        new(
            false,
            false,
            null,
            null,
            false,
            null,
            null,
            null,
            null,
            null,
            null,
            [],
            0,
            false);

    public static CloudLinkStatusSnapshot Disabled(bool hasConsent) =>
        new(
            false,
            hasConsent,
            hasConsent ? new DateTime(2026, 11, 17, 2, 0, 0, DateTimeKind.Utc) : null,
            null,
            false,
            null,
            null,
            null,
            null,
            null,
            null,
            [],
            0,
            false);

    public static CloudLinkStatusSnapshot Pending(
        DateTime expiresAtUtc,
        Guid sessionId,
        string code = "ABCD2345",
        int pendingSyncEvents = 3) =>
        new(
            true,
            true,
            new DateTime(2026, 11, 17, 2, 0, 0, DateTimeKind.Utc),
            BaseUrl,
            false,
            null,
            null,
            null,
            null,
            null,
            new CloudPairingInfo(
                sessionId,
                code,
                expiresAtUtc,
                $"edufocus://pair?v=1&s={sessionId:D}&c={code}",
                BaseUrl),
            [],
            pendingSyncEvents,
            true);

    public static CloudLinkStatusSnapshot Linked(
        DateTime lastSyncAtUtc,
        string phoneName = "Pixel của mẹ",
        string? lastError = null,
        bool remoteStatusChecked = true,
        int pendingSyncEvents = 2) =>
        new(
            true,
            true,
            new DateTime(2026, 11, 17, 2, 0, 0, DateTimeKind.Utc),
            BaseUrl,
            true,
            Guid.NewGuid(),
            Guid.NewGuid(),
            new DateTime(2026, 11, 16, 9, 30, 0, DateTimeKind.Utc),
            lastSyncAtUtc,
            lastError,
            null,
            [
                new CloudLinkedDevice(Guid.NewGuid(), "Desktop", "PC phòng học", "windows", false),
                new CloudLinkedDevice(Guid.NewGuid(), "Mobile", phoneName, "android", false)
            ],
            pendingSyncEvents,
            remoteStatusChecked);
}
