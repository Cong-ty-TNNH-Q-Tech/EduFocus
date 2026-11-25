using EduFocus.Parent.Core;
using EduFocus.Parent.Core.Pairing;
using EduFocus.Parent.Core.Presentation;
using EduFocus.Parent.Core.Services;
using EduFocus.Parent.Core.Settings;
using EduFocus.Parent.Tests.TestSupport;

namespace EduFocus.Parent.Tests.Pairing;

public sealed class LinkStateRestorerTests
{
    private static readonly Guid FamilyId = Guid.Parse("2b9f6f70-1f61-4d3e-9d7d-3a2f6b5c4d11");
    private static readonly Guid DeviceId = Guid.Parse("8e2c6b1a-7f3d-4a55-8c1f-0d9e7b6a5c33");

    [Fact]
    public async Task Restores_a_complete_link()
    {
        var (restorer, tokens, state, settings, store) = Create(token: "efm_token");

        var linked = await restorer.RestoreAsync();

        Assert.True(linked);
        Assert.True(state.IsLinked);
        Assert.Equal(FamilyId, state.FamilyId);
        Assert.Equal(DeviceId, state.DeviceId);
        Assert.Equal("https://cloud.example.com", state.CloudBaseUrl);
        Assert.Equal("Student PC", state.DesktopName);
        Assert.Equal("Asia/Ho_Chi_Minh", state.TimeZone);
        Assert.Equal("efm_token", tokens.Token);
        Assert.NotNull(store.Get(ParentDataCache.DailyKey));
    }

    [Fact]
    public async Task Startup_removes_legacy_fcm_token_without_unlinking()
    {
        var (restorer, _, state, _, store) = Create(token: "efm_token");
        store.Set("edufocus.parent.push_token", "old-fcm-token");

        var linked = await restorer.RestoreAsync();

        Assert.True(linked);
        Assert.True(state.IsLinked);
        Assert.Null(store.Get("edufocus.parent.push_token"));
    }

    [Fact]
    public async Task Missing_token_clears_link_metadata_but_keeps_server()
    {
        var (restorer, _, state, settings, store) = Create(token: null);

        var linked = await restorer.RestoreAsync();

        Assert.False(linked);
        Assert.False(state.IsLinked);
        Assert.Null(settings.FamilyId);
        Assert.Null(settings.DeviceId);
        Assert.Equal("https://cloud.example.com", settings.CloudBaseUrl);
        Assert.Equal("https://cloud.example.com", state.CloudBaseUrl);
        Assert.Null(store.Get(ParentDataCache.DailyKey));
    }

    [Fact]
    public async Task Token_without_link_metadata_is_cleared()
    {
        var (restorer, tokens, state, settings, store) = Create(token: "efm_orphan", withLink: false);

        var linked = await restorer.RestoreAsync();

        Assert.False(linked);
        Assert.Null(tokens.Token);
        Assert.False(state.IsLinked);
        Assert.Null(settings.FamilyId);
        Assert.Equal("https://cloud.example.com", state.CloudBaseUrl);
        Assert.Null(store.Get(ParentDataCache.DailyKey));
    }

    [Fact]
    public async Task Unreadable_token_store_resets_the_link_and_cache()
    {
        var tokens = new ThrowingTokenStore();
        var (restorer, state, settings, store) = Build(tokens, withLink: true);

        var linked = await restorer.RestoreAsync();

        Assert.False(linked);
        Assert.False(state.IsLinked);
        Assert.Null(state.FamilyId);
        Assert.Null(state.DeviceId);
        Assert.Null(settings.FamilyId);
        Assert.Null(settings.DeviceId);
        Assert.Equal("https://cloud.example.com", state.CloudBaseUrl);
        Assert.Null(store.Get(ParentDataCache.DailyKey));
        Assert.Equal(1, tokens.ClearCalls);
    }

    [Fact]
    public async Task Unreadable_token_store_still_resets_when_clearing_fails()
    {
        var tokens = new ThrowingTokenStore(clearThrows: true);
        var (restorer, state, settings, store) = Build(tokens, withLink: true);

        var linked = await restorer.RestoreAsync();

        Assert.False(linked);
        Assert.False(state.IsLinked);
        Assert.Null(settings.FamilyId);
        Assert.Null(store.Get(ParentDataCache.DailyKey));
    }

    private static (LinkStateRestorer Restorer, MemoryTokenStore Tokens, AppState State, ParentSettingsStore Settings, FakeKeyValueStore Store)
        Create(string? token, bool withLink = true)
    {
        var tokens = new MemoryTokenStore(token);
        var built = Build(tokens, withLink);
        return (built.Restorer, tokens, built.State, built.Settings, built.Store);
    }

    private static (LinkStateRestorer Restorer, AppState State, ParentSettingsStore Settings, FakeKeyValueStore Store)
        Build(ITokenStore tokens, bool withLink)
    {
        var store = new FakeKeyValueStore();
        var state = new AppState();
        var settings = new ParentSettingsStore(store)
        {
            CloudBaseUrl = "https://cloud.example.com",
        };

        if (withLink)
        {
            settings.FamilyId = FamilyId;
            settings.DeviceId = DeviceId;
            settings.DesktopName = "Student PC";
            settings.TimeZoneId = "Asia/Ho_Chi_Minh";
        }

        store.Set(ParentDataCache.DailyKey, "{\"cached\":true}");

        var restorer = new LinkStateRestorer(
            tokens,
            settings,
            new ParentDataCache(store),
            state);
        return (restorer, state, settings, store);
    }

    private sealed class ThrowingTokenStore(bool clearThrows = false) : ITokenStore
    {
        public int ClearCalls { get; private set; }

        public Task<string?> GetTokenAsync(CancellationToken cancellationToken = default) =>
            throw new InvalidOperationException("The Android Keystore key is missing.");

        public Task SaveTokenAsync(string token, CancellationToken cancellationToken = default) =>
            Task.CompletedTask;

        public Task ClearAsync(CancellationToken cancellationToken = default)
        {
            ClearCalls++;
            if (clearThrows)
                throw new InvalidOperationException("SecureStorage is unavailable.");

            return Task.CompletedTask;
        }
    }
}
