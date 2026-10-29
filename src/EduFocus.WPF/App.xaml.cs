using System.Windows;
using EduFocus.Domain.Protection;
using EduFocus.Infrastructure;
using EduFocus.Ports.Inbound;
using EduFocus.Ports.Outbound.Persistence;
using EduFocus.Ports.Outbound.Services;
using EduFocus.SqliteDataAccess.Persistence;
using EduFocus.SqliteDataAccess.Repositories;
using EduFocus.UseCases.Auth;
using EduFocus.UseCases.Blocking;
using EduFocus.UseCases.Lifecycle;
using EduFocus.UseCases.Onboarding;
using EduFocus.UseCases.Protection;
using EduFocus.UseCases.Schedule;
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

        // Install or upgrade the built-in rules before protection starts so the
        // first reconcile already sees the default blocklist.
        try
        {
            var seed = await Services.GetRequiredService<IBlockRuleSeedUseCase>().EnsureSeededAsync();
            if (seed.Changed)
            {
                logger.Info(
                    LogCategories.Protection,
                    FormattableString.Invariant(
                        $"Built-in block rules v{seed.SeedVersion} seeded: {seed.Added} added, {seed.Updated} updated."));
            }
        }
        catch (Exception ex)
        {
            logger.Error(LogCategories.Protection, "Built-in block rule seeding failed.", ex);
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

        // Apply the schedule once before any UI shows so a window that started
        // while the app was closed is reflected in the tray and main window.
        var scheduleScheduler = Services.GetRequiredService<ScheduleSchedulerService>();
        await scheduleScheduler.EvaluateNowAsync();

        var mainWindow = Services.GetRequiredService<MainWindow>();
        MainWindow = mainWindow;
        ShutdownMode = ShutdownMode.OnMainWindowClose;
        mainWindow.Show();

        Services.GetRequiredService<TrayIconService>().Start();
        scheduleScheduler.Start();

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
        services.AddSingleton<IScheduleWindowRepository, SqliteScheduleWindowRepository>();
        services.AddSingleton<IBlockRuleRepository, SqliteBlockRuleRepository>();
        services.AddSingleton<IBlockedAttemptRepository, SqliteBlockedAttemptRepository>();
        services.AddSingleton(BuiltInRuleCatalog.Default);
        services.AddSingleton<IBlockRuleSeedUseCase, BlockRuleSeedService>();
        services.AddSingleton<IBlockRuleUseCase, BlockRuleService>();
        services.AddSingleton<IParentAuthUseCase, ParentAuthUseCase>();
        services.AddSingleton<ParentSessionState>();
        services.AddSingleton<IParentAuthorizer>(provider => provider.GetRequiredService<ParentSessionState>());
        services.AddSingleton<IParentModeUseCase, ParentModeService>();
        services.AddSingleton<IOnboardingUseCase, OnboardingService>();
        services.AddSingleton<IProtectionUseCase, ProtectionService>();
        services.AddSingleton<IScheduleUseCase, ScheduleService>();
        services.AddSingleton<IAppLifecycleUseCase, AppLifecycleService>();
        services.AddSingleton<DatabaseBootstrapper>();

        services.AddSingleton<UpdaterService>();
        services.AddSingleton<UpdateViewModel>();
        services.AddSingleton<ProtectionStatusViewModel>();
        services.AddSingleton<ScheduleViewModel>();
        services.AddSingleton<BlocklistViewModel>();
        services.AddSingleton<MainWindowViewModel>();
        services.AddSingleton<TrayIconService>();
        services.AddSingleton<ScheduleSchedulerService>();
        services.AddSingleton<AppExitState>();
        services.AddSingleton<MainWindow>();
        services.AddTransient<ParentModeViewModel>();
        services.AddTransient<ParentModeWindow>();
        services.AddTransient<OnboardingViewModel>();
        services.AddTransient<OnboardingWindow>();
        services.AddTransient<ExitViewModel>();
        services.AddTransient<ExitWindow>();
        services.AddSingleton<Func<ParentModeWindow>>(provider =>
            () => provider.GetRequiredService<ParentModeWindow>());
        services.AddSingleton<Func<ExitWindow>>(provider =>
            () => provider.GetRequiredService<ExitWindow>());
    }

    protected override void OnSessionEnding(SessionEndingCancelEventArgs e)
    {
        var logger = Services?.GetService<IAppLogger>();

        try
        {
            // Best effort with a bounded wait so Windows is never blocked; a
            // cancelled write is healed by ReconcileAsync at the next startup.
            if (Services?.GetService<IAppLifecycleUseCase>() is { } lifecycle)
            {
                var recording = Task.Run(() => lifecycle.NotifySystemShutdownAsync());
                if (!recording.Wait(TimeSpan.FromSeconds(2)))
                    logger?.Warning(LogCategories.App, "Windows shutdown recording timed out.");
            }
        }
        catch (Exception ex)
        {
            logger?.Error(LogCategories.App, "Windows shutdown recording failed.", ex);
        }

        base.OnSessionEnding(e);
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
