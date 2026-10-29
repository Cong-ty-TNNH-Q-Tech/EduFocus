using System.Windows;
using EduFocus.Infrastructure;
using EduFocus.Ports.Inbound;
using EduFocus.Ports.Outbound.Persistence;
using EduFocus.Ports.Outbound.Services;
using EduFocus.SqliteDataAccess.Persistence;
using EduFocus.SqliteDataAccess.Repositories;
using EduFocus.UseCases.Auth;
using EduFocus.UseCases.Onboarding;
using EduFocus.UseCases.Protection;
using EduFocus.WPF.Services;
using EduFocus.WPF.ViewModels;
using Microsoft.Extensions.DependencyInjection;

namespace EduFocus.WPF;

public partial class App : Application
{
    public static ServiceProvider Services { get; private set; } = null!;
    private static System.Threading.Mutex? _mutex;
    private static IDisposable? _correlationScope;

    protected override async void OnStartup(StartupEventArgs e)
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

        var correlation = Services.GetRequiredService<ICorrelationContext>();
        _correlationScope = correlation.BeginScope(Guid.NewGuid());

        var logger = Services.GetRequiredService<IAppLogger>();
        logger.Info(LogCategories.App, "====== EduFocus started ======");

        var onboardingWindow = Services.GetRequiredService<OnboardingWindow>();
        if (onboardingWindow.ShowDialog() != true)
        {
            logger.Info(LogCategories.App, "Onboarding was not completed; exiting.");
            Shutdown();
            return;
        }

        // Restore protection before any UI shows so the tray, window and
        // persisted state never disagree after a crash or restart.
        var protectionMonitor = Services.GetRequiredService<ProtectionStatusViewModel>();
        try
        {
            var reconciled = await Services.GetRequiredService<IProtectionUseCase>().ReconcileAsync();
            protectionMonitor.Apply(reconciled);
        }
        catch (Exception ex)
        {
            logger.Error(LogCategories.Protection, "Protection reconciliation at startup failed.", ex);
            protectionMonitor.ReportFailure(
                "Không thể khôi phục trạng thái bảo vệ sau khi khởi động. Hãy mở Parent Mode để kiểm tra.");
        }

        var mainWindow = Services.GetRequiredService<MainWindow>();
        MainWindow = mainWindow;
        ShutdownMode = ShutdownMode.OnMainWindowClose;
        mainWindow.Show();

        Services.GetRequiredService<TrayIconService>().Start();

        // Background update check — non-blocking, silent on failure
        _ = Services.GetRequiredService<MainWindowViewModel>().Update.CheckForUpdateAsync();
    }

    private static void ConfigureServices(IServiceCollection services)
    {
        services.AddInfrastructureServices();

        services.AddSingleton(new EduFocusDatabaseOptions());
        services.AddSingleton<SqliteDatabaseInitializer>();
        services.AddSingleton<IAppSettingsRepository, SqliteAppSettingsRepository>();
        services.AddSingleton<IParentCredentialRepository, SqliteParentCredentialRepository>();
        services.AddSingleton<IAuditLogRepository, SqliteAuditLogRepository>();
        services.AddSingleton<IStudySessionRepository, SqliteStudySessionRepository>();
        services.AddSingleton<IParentAuthUseCase, ParentAuthUseCase>();
        services.AddSingleton<ParentSessionState>();
        services.AddSingleton<IParentAuthorizer>(provider => provider.GetRequiredService<ParentSessionState>());
        services.AddSingleton<IParentModeUseCase, ParentModeService>();
        services.AddSingleton<IOnboardingUseCase, OnboardingService>();
        services.AddSingleton<IProtectionUseCase, ProtectionService>();
        services.AddSingleton<DatabaseBootstrapper>();

        services.AddSingleton<UpdaterService>();
        services.AddSingleton<UpdateViewModel>();
        services.AddSingleton<ProtectionStatusViewModel>();
        services.AddSingleton<MainWindowViewModel>();
        services.AddSingleton<TrayIconService>();
        services.AddSingleton<MainWindow>();
        services.AddTransient<ParentModeViewModel>();
        services.AddTransient<ParentModeWindow>();
        services.AddTransient<OnboardingViewModel>();
        services.AddTransient<OnboardingWindow>();
        services.AddSingleton<Func<ParentModeWindow>>(provider =>
            () => provider.GetRequiredService<ParentModeWindow>());
    }

    protected override void OnExit(ExitEventArgs e)
    {
        try
        {
            Services?.GetService<IAppLogger>()?.Info(LogCategories.App, "====== EduFocus exited ======");
        }
        catch { /* best effort */ }
        finally
        {
            _correlationScope?.Dispose();
        }
        Services?.Dispose();
        _mutex?.ReleaseMutex();
        _mutex?.Dispose();
        base.OnExit(e);
    }

    private void App_DispatcherUnhandledException(object sender, System.Windows.Threading.DispatcherUnhandledExceptionEventArgs e)
    {
        try
        {
            Services?.GetService<IAppLogger>()?.Error(LogCategories.UI, "Unhandled Dispatcher exception caught.", e.Exception);
        }
        catch { }
        e.Handled = true;
    }

    private void TaskScheduler_UnobservedTaskException(object? sender, System.Threading.Tasks.UnobservedTaskExceptionEventArgs e)
    {
        try
        {
            Services?.GetService<IAppLogger>()?.Error(LogCategories.UI, "Unobserved task exception caught.", e.Exception);
        }
        catch { }
        e.SetObserved();
    }
}
