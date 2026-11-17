using EduFocus.Parent.Core.Settings;
using EduFocus.Parent.Tests.TestSupport;

namespace EduFocus.Parent.Tests.Settings;

public sealed class ParentSettingsStoreTests
{
    [Fact]
    public void Round_trips_typed_values()
    {
        var store = new ParentSettingsStore(new FakeKeyValueStore());
        var familyId = Guid.NewGuid();
        var deviceId = Guid.NewGuid();

        store.CloudBaseUrl = "https://cloud.example.com/";
        store.FamilyId = familyId;
        store.DeviceId = deviceId;
        store.DesktopName = " Student PC ";
        store.TimeZoneId = "Asia/Ho_Chi_Minh";

        Assert.Equal("https://cloud.example.com", store.CloudBaseUrl);
        Assert.Equal(familyId, store.FamilyId);
        Assert.Equal(deviceId, store.DeviceId);
        Assert.Equal("Student PC", store.DesktopName);
        Assert.Equal("Asia/Ho_Chi_Minh", store.TimeZoneId);
    }

    [Fact]
    public void Clear_link_keeps_server_configuration()
    {
        var store = new ParentSettingsStore(new FakeKeyValueStore())
        {
            CloudBaseUrl = "https://cloud.example.com",
            FamilyId = Guid.NewGuid(),
            DeviceId = Guid.NewGuid(),
            DesktopName = "Student PC",
            TimeZoneId = "Asia/Ho_Chi_Minh",
            PushToken = "fcm-token",
            PushAppVersion = "0.1.0",
        };

        store.ClearLink();

        Assert.Equal("https://cloud.example.com", store.CloudBaseUrl);
        Assert.Null(store.FamilyId);
        Assert.Null(store.DeviceId);
        Assert.Null(store.DesktopName);
        Assert.Null(store.TimeZoneId);
        Assert.Null(store.PushToken);
        Assert.Null(store.PushAppVersion);
    }

    [Fact]
    public void Invalid_guid_is_ignored()
    {
        var keyValue = new FakeKeyValueStore();
        keyValue.Set(ParentSettingsStore.FamilyIdKey, "not-a-guid");
        var store = new ParentSettingsStore(keyValue);

        Assert.Null(store.FamilyId);
    }
}
