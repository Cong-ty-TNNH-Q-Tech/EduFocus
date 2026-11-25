using EduFocus.Parent.Core.Push;
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
            PushAppVersion = "0.1.0",
        };
        store.SetPushEndpoint(new PushEndpointRegistration(
            "https://push.example.com/sub/device-1",
            "BAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAA",
            "AAAAAAAAAAAAAAAAAAAAAA"));

        store.ClearLink();

        Assert.Equal("https://cloud.example.com", store.CloudBaseUrl);
        Assert.Null(store.FamilyId);
        Assert.Null(store.DeviceId);
        Assert.Null(store.DesktopName);
        Assert.Null(store.TimeZoneId);
        Assert.Null(store.GetPushEndpoint());
        Assert.Null(store.PushAppVersion);
    }

    [Fact]
    public void Legacy_fcm_token_is_removed_from_storage()
    {
        var keyValue = new FakeKeyValueStore();
        keyValue.Set("edufocus.parent.push_token", "old-fcm-token");
        var store = new ParentSettingsStore(keyValue);

        store.ClearLink();

        Assert.Null(keyValue.Get("edufocus.parent.push_token"));
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
