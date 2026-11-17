using EduFocus.Parent.Core;
using EduFocus.Parent.Core.Pairing;
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
        var (restorer, tokens, state, settings) = Create(token: "efm_token");

        var linked = await restorer.RestoreAsync();

        Assert.True(linked);
        Assert.True(state.IsLinked);
        Assert.Equal(FamilyId, state.FamilyId);
        Assert.Equal(DeviceId, state.DeviceId);
        Assert.Equal("https://cloud.example.com", state.CloudBaseUrl);
        Assert.Equal("Student PC", state.DesktopName);
        Assert.Equal("Asia/Ho_Chi_Minh", state.TimeZone);
        Assert.Equal("efm_token", tokens.Token);
    }

    [Fact]
    public async Task Missing_token_clears_link_metadata_but_keeps_server()
    {
        var (restorer, _, state, settings) = Create(token: null);

        var linked = await restorer.RestoreAsync();

        Assert.False(linked);
        Assert.False(state.IsLinked);
        Assert.Null(settings.FamilyId);
        Assert.Null(settings.DeviceId);
        Assert.Equal("https://cloud.example.com", settings.CloudBaseUrl);
        Assert.Equal("https://cloud.example.com", state.CloudBaseUrl);
    }

    [Fact]
    public async Task Token_without_link_metadata_is_cleared()
    {
        var (restorer, tokens, state, settings) = Create(token: "efm_orphan", withLink: false);

        var linked = await restorer.RestoreAsync();

        Assert.False(linked);
        Assert.Null(tokens.Token);
        Assert.False(state.IsLinked);
        Assert.Null(settings.FamilyId);
        Assert.Equal("https://cloud.example.com", state.CloudBaseUrl);
    }

    private static (LinkStateRestorer Restorer, MemoryTokenStore Tokens, AppState State, ParentSettingsStore Settings)
        Create(string? token, bool withLink = true)
    {
        var tokens = new MemoryTokenStore(token);
        var state = new AppState();
        var settings = new ParentSettingsStore(new FakeKeyValueStore())
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

        return (new LinkStateRestorer(tokens, settings, state), tokens, state, settings);
    }
}
