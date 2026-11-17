using EduFocus.Parent.Core;
using EduFocus.Parent.Core.Presentation;
using EduFocus.Parent.Core.Settings;
using EduFocus.Parent.Tests.TestSupport;

namespace EduFocus.Parent.Tests.Presentation;

public sealed class SessionExpiryServiceTests
{
    [Fact]
    public async Task Unauthorized_clears_link_and_cache_and_raises_once()
    {
        var keyValue = new FakeKeyValueStore();
        var settings = new ParentSettingsStore(keyValue)
        {
            FamilyId = Guid.NewGuid(),
            DeviceId = Guid.NewGuid(),
            DesktopName = "Student PC",
            TimeZoneId = "Asia/Ho_Chi_Minh",
        };
        var tokens = new MemoryTokenStore("efm_token");
        var state = new AppState { IsLinked = true, FamilyId = settings.FamilyId, DeviceId = settings.DeviceId };
        var cache = new ParentDataCache(keyValue);
        cache.SaveDaily([TestSummaries.Daily(new DateOnly(2026, 11, 17))]);
        var session = new SessionExpiryService(tokens, state, settings, cache);
        var raised = 0;
        session.Expired += () => raised++;

        await session.HandleUnauthorizedAsync();
        await session.HandleUnauthorizedAsync();

        Assert.Equal(1, raised);
        Assert.Null(tokens.Token);
        Assert.False(state.IsLinked);
        Assert.Null(settings.FamilyId);
        Assert.False(cache.TryGetDaily(out _, out _));
    }

    [Fact]
    public async Task Reset_allows_the_event_again_after_repairing()
    {
        var keyValue = new FakeKeyValueStore();
        var settings = new ParentSettingsStore(keyValue);
        var tokens = new MemoryTokenStore("efm_token");
        var state = new AppState { IsLinked = true };
        var session = new SessionExpiryService(tokens, state, settings, new ParentDataCache(keyValue));
        var raised = 0;
        session.Expired += () => raised++;

        await session.HandleUnauthorizedAsync();
        session.Reset();
        state.IsLinked = true;
        await session.HandleUnauthorizedAsync();

        Assert.Equal(2, raised);
    }

    [Fact]
    public async Task Clear_local_does_not_raise_the_expiry_event()
    {
        var keyValue = new FakeKeyValueStore();
        var session = new SessionExpiryService(
            new MemoryTokenStore("efm_token"),
            new AppState { IsLinked = true },
            new ParentSettingsStore(keyValue),
            new ParentDataCache(keyValue));
        var raised = 0;
        session.Expired += () => raised++;

        await session.ClearLocalAsync();

        Assert.Equal(0, raised);
    }
}
