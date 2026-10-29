using System.Windows;
using EduFocus.Infrastructure;
using EduFocus.Ports.Outbound.Services;
using EduFocus.WPF.Services;
using EduFocus.WPF.ViewModels;
using Microsoft.Extensions.DependencyInjection;

namespace EduFocus.WPF;

public partial class App : Application
{
    public static ServiceProvider Services { get; private set; } = null!;
    private static System.Threading.Mutex? _mutex;

    protected override void OnStartup(StartupEventArgs e)
    {
        const string appName = "EduFocus_SingleInstance_Mutex";
        _mutex = new System.Threading.Mutex(true, appName, out bool createdNew);
        if (!createdNew)
        {
            MessageBox.Show("Ứng dụng EduFocus đang được chạy.", "Thông báo", MessageBoxButton.OK, MessageBoxImage.Information);
            Application.Current.Shutdown();
            return;
        }

        this.DispatcherUnhandledException += App_DispatcherUnhandledException;
        System.Threading.Tasks.TaskScheduler.UnobservedTaskException += TaskScheduler_UnobservedTaskException;

        base.OnStartup(e);

        var serviceCollection = new ServiceCollection();
        ConfigureServices(serviceCollection);
        Services = serviceCollection.BuildServiceProvider();

        var mainWindow = Services.GetRequiredService<MainWindow>();
        MainWindow = mainWindow;
        mainWindow.Show();

        var logger = Services.GetRequiredService<IAppLogger>();
        logger.Info("====== EduFocus started ======");

        // Background update check — non-blocking, silent on failure
        _ = Services.GetRequiredService<UpdateViewModel>().CheckForUpdateAsync();
    }

    private static void ConfigureServices(IServiceCollection services)
    {
        services.AddInfrastructureServices();
        services.AddSingleton<UpdaterService>();
        services.AddSingleton<UpdateViewModel>();
        services.AddSingleton<MainWindow>();
    }

    protected override void OnExit(ExitEventArgs e)
    {
        try
        {
            Services?.GetService<IAppLogger>()?.Info("====== EduFocus exited ======");
        }
        catch { /* best effort */ }
        Services?.Dispose();
        _mutex?.ReleaseMutex();
        _mutex?.Dispose();
        base.OnExit(e);
    }

    private void App_DispatcherUnhandledException(object sender, System.Windows.Threading.DispatcherUnhandledExceptionEventArgs e)
    {
        try
        {
            Services?.GetService<IAppLogger>()?.Error("[UI] Unhandled Dispatcher exception caught.", e.Exception);
        }
        catch { }
        e.Handled = true;
    }

    private void TaskScheduler_UnobservedTaskException(object? sender, System.Threading.Tasks.UnobservedTaskExceptionEventArgs e)
    {
        try
        {
            Services?.GetService<IAppLogger>()?.Error("[BG] Unobserved task exception caught.", e.Exception);
        }
        catch { }
        e.SetObserved();
    }
}
