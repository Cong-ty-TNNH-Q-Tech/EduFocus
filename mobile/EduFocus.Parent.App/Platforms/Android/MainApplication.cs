using Android.App;
using Android.Runtime;

namespace EduFocus.Parent.App;

[Application]
public class MainApplication : MauiApplication
{
    protected MainApplication(nint handle, JniHandleOwnership ownership)
        : base(handle, ownership)
    {
    }

    protected override MauiApp CreateMauiApp() => MauiProgram.CreateMauiApp();
}
