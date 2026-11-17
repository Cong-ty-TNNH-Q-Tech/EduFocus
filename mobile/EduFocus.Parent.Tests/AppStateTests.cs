using EduFocus.Parent.Core;

namespace EduFocus.Parent.Tests;

public sealed class AppStateTests
{
    [Fact]
    public void Changing_link_state_raises_property_changed()
    {
        var state = new AppState();
        var changed = new List<string?>();
        state.PropertyChanged += (_, args) => changed.Add(args.PropertyName);

        state.IsLinked = true;

        Assert.True(state.IsLinked);
        Assert.Contains(nameof(AppState.IsLinked), changed);
    }
}
