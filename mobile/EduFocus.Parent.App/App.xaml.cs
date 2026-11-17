using EduFocus.Parent.App.Services;

namespace EduFocus.Parent.App;

public partial class App : Application
{
    public App()
    {
        InitializeComponent();
    }

    protected override Window CreateWindow(IActivationState? activationState)
    {
        var window = new Window(new AppShell());
        window.Activated += (_, _) => AppLifecycleState.IsForeground = true;
        window.Resumed += (_, _) => AppLifecycleState.IsForeground = true;
        window.Deactivated += (_, _) => AppLifecycleState.IsForeground = false;
        window.Stopped += (_, _) => AppLifecycleState.IsForeground = false;
        return window;
    }
}
