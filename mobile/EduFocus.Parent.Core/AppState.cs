using CommunityToolkit.Mvvm.ComponentModel;

namespace EduFocus.Parent.Core;

public sealed partial class AppState : ObservableObject
{
    [ObservableProperty]
    private string? cloudBaseUrl;

    [ObservableProperty]
    private bool isLinked;

    [ObservableProperty]
    private Guid? familyId;

    [ObservableProperty]
    private Guid? deviceId;

    [ObservableProperty]
    private string? timeZone;

    [ObservableProperty]
    private string? desktopName;
}
