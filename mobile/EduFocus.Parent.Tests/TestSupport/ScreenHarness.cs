using EduFocus.Parent.Core;
using EduFocus.Parent.Core.Presentation;
using EduFocus.Parent.Core.Settings;

namespace EduFocus.Parent.Tests.TestSupport;

internal sealed class ScreenHarness
{
    public static readonly DateTimeOffset Now = new(2026, 11, 17, 3, 0, 0, TimeSpan.Zero);

    public static readonly DateOnly Today = new(2026, 11, 17);

    public ScreenHarness()
    {
        State = new AppState
        {
            CloudBaseUrl = "https://cloud.example.com",
            IsLinked = true,
            FamilyId = Guid.NewGuid(),
            DeviceId = Guid.NewGuid(),
            TimeZone = "Asia/Ho_Chi_Minh",
            DesktopName = "Student PC",
        };
        Settings = new ParentSettingsStore(KeyValue)
        {
            FamilyId = State.FamilyId,
            DeviceId = State.DeviceId,
            DesktopName = State.DesktopName,
            TimeZoneId = State.TimeZone,
        };
        Cache = new ParentDataCache(KeyValue, () => Now);
        Session = new SessionExpiryService(Tokens, State, Settings, Cache);
    }

    public FakeKeyValueStore KeyValue { get; } = new();

    public MemoryTokenStore Tokens { get; } = new("efm_token");

    public AppState State { get; }

    public ParentSettingsStore Settings { get; }

    public ParentDataCache Cache { get; }

    public SessionExpiryService Session { get; }

    public FakeParentDataService Api { get; } = new();

    public OverviewViewModel Overview() =>
        new(Api, Cache, State, Session, () => Now);

    public ReportsViewModel Reports() =>
        new(Api, Cache, State, Session, () => Now);

    public NotificationsViewModel Notifications() =>
        new(Api, Cache, State, Session);
}
