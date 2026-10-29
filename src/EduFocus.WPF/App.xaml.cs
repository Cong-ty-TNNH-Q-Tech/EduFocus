using System.IO;
using System.Windows;
using EduFocus.Domain.Attention;
using EduFocus.Domain.Protection;
using EduFocus.Infrastructure;
using EduFocus.Infrastructure.Attention;
using EduFocus.Infrastructure.Attention.Camera;
using EduFocus.Infrastructure.Watchdog;
using EduFocus.Ports.DTOs;
using EduFocus.Ports.Inbound;
using EduFocus.Ports.Outbound.Persistence;
using EduFocus.Ports.Outbound.Services;
using EduFocus.SqliteDataAccess.Persistence;
using EduFocus.SqliteDataAccess.Repositories;
using EduFocus.UseCases.Activity;
using EduFocus.UseCases.Attention;
using EduFocus.UseCases.Auth;
using EduFocus.UseCases.Blocking;
using EduFocus.UseCases.Lifecycle;
using EduFocus.UseCases.Onboarding;
using EduFocus.UseCases.Privacy;
using EduFocus.UseCases.Protection;
using EduFocus.UseCases.Reports;
using EduFocus.UseCases.Retention;
using EduFocus.UseCases.Schedule;
using EduFocus.UseCases.Sessions;
using EduFocus.UseCases.Watchdog;
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
        base.OnStartup(e);

        this.DispatcherUnhandledException += App_DispatcherUnhandledException;
        System.Threading.Tasks.TaskScheduler.UnobservedTaskException += TaskScheduler_UnobservedTaskException;

        var command = WatchdogCommandLine.Parse(e.Args);
        if (command.Mode != WatchdogCommandMode.App)
        {
            ShutdownMode = ShutdownMode.OnExplicitShutdown;
            await RunWatchdogCommandAsync(command);
            return;
        }

        _mutex = new System.Threading.Mutex(true, WatchdogNames.AppInstanceMutex, out bool createdNew);
        if (!createdNew)
        {
            MessageBox.Show("Ứng dụng EduFocus đang được chạy.", "Thông báo", MessageBoxButton.OK, MessageBoxImage.Information);
            Application.Current.Shutdown();
            return;
        }

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

        // Publish the liveness beacon before the watchdog can be spawned so its
        // first tick already sees a fresh heartbeat.
        Services.GetRequiredService<WatchdogHeartbeatPublisher>().Start();

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

        // Startup reconcile also installs/repairs the watchdog tasks and the
        // supervisor process; later protection transitions go through the
        // protection decorator.
        try
        {
            await Services.GetRequiredService<IWatchdogCoordinator>().SynchronizeAsync();
        }
        catch (Exception ex)
        {
            logger.Error(LogCategories.Watchdog, "Watchdog startup synchronization failed.", ex);
        }

        var mainWindow = Services.GetRequiredService<MainWindow>();
        MainWindow = mainWindow;
        ShutdownMode = ShutdownMode.OnMainWindowClose;
        mainWindow.Show();

        Services.GetRequiredService<TrayIconService>().Start();
        scheduleScheduler.Start();
        Services.GetRequiredService<RetentionSchedulerService>().Start();
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
        services.AddSingleton<IAttentionEventRepository, SqliteAttentionEventRepository>();
        services.AddSingleton<IScheduleWindowRepository, SqliteScheduleWindowRepository>();
        services.AddSingleton<IBlockRuleRepository, SqliteBlockRuleRepository>();
        services.AddSingleton<IBlockedAttemptRepository, SqliteBlockedAttemptRepository>();
        services.AddSingleton<IActivitySampleRepository, SqliteActivitySampleRepository>();
        services.AddSingleton<IRetentionRepository, SqliteRetentionRepository>();
        services.AddSingleton<IPrivacyDataRepository, SqlitePrivacyDataRepository>();
        services.AddSingleton<ISystemPowerEventPort, WindowsSystemPowerEventPort>();
        services.AddSingleton(new StudySessionHeartbeatOptions());
        services.AddSingleton<IStudySessionHeartbeatUseCase, StudySessionHeartbeatService>();
        services.AddSingleton(new ActivityTrackingOptions());
        services.AddSingleton<IBrowserUrlProviderPort, UiAutomationBrowserUrlProvider>();
        services.AddSingleton<IActivityUseCase, ActivityTrackingService>();
        services.AddSingleton<ICameraDeviceCatalog, WindowsCameraDeviceCatalog>();
        services.AddSingleton(new CameraPortOptions());
        services.AddSingleton<ICameraPort, OpenCvCameraPort>();
        services.AddSingleton(new CameraOptions());
        services.AddSingleton<ICameraSessionUseCase, CameraSessionService>();
        services.AddSingleton<ICameraPlatformInfo, WindowsCameraPlatformInfo>();
        services.AddSingleton(new CameraCheckOptions());
        services.AddSingleton<ICameraCheckUseCase, CameraCheckService>();
        // The check needs the inference model; resolve it only when a parent
        // actually runs a check so a missing model cannot break startup.
        services.AddSingleton<Func<ICameraCheckUseCase>>(provider =>
            () => provider.GetRequiredService<ICameraCheckUseCase>());
        services.AddSingleton(new AttentionAnalyzerOptions());
        services.AddSingleton(AttentionPipelineOptions.Default);
        services.AddSingleton<IAttentionFrameAnalyzer>(provider =>
            AttentionAnalyzerFactory.Create(
                "yunet",
                provider.GetRequiredService<AttentionAnalyzerOptions>(),
                Path.Combine(AppContext.BaseDirectory, "models")));
        services.AddSingleton<IAttentionPipelineUseCase, AttentionPipelineService>();
        services.AddSingleton(new AttentionThresholds());
        services.AddSingleton(AttentionMonitoringOptions.Default);
        services.AddSingleton<IAttentionMonitorUseCase, AttentionMonitorService>();
        services.AddSingleton(BuiltInRuleCatalog.Default);
        services.AddSingleton<IBlockRuleSeedUseCase, BlockRuleSeedService>();
        services.AddSingleton<IBlockRuleUseCase, BlockRuleService>();
        services.AddSingleton<IParentAuthUseCase, ParentAuthUseCase>();
        services.AddSingleton<ParentSessionState>();
        services.AddSingleton<IParentAuthorizer>(provider => provider.GetRequiredService<ParentSessionState>());
        services.AddSingleton<IParentModeUseCase, ParentModeService>();
        services.AddSingleton<IOnboardingUseCase, OnboardingService>();
        services.AddSingleton(new WatchdogOptions());
        services.AddSingleton<IWatchdogCoordinator, WatchdogCoordinator>();
        services.AddSingleton<ProtectionService>();
        services.AddSingleton<IProtectionUseCase>(provider =>
            new WatchdogAwareProtectionService(
                provider.GetRequiredService<ProtectionService>(),
                provider.GetRequiredService<IWatchdogCoordinator>()));
        services.AddSingleton<IDashboardUseCase, DashboardService>();
        services.AddSingleton<IReportUseCase, ReportService>();
        services.AddSingleton<IReportExportUseCase, ReportExportService>();
        services.AddSingleton<IScheduleUseCase, ScheduleService>();
        services.AddSingleton(new RetentionOptions());
        services.AddSingleton<IRetentionUseCase, RetentionService>();
        services.AddSingleton(new DataDeletionOptions());
        services.AddSingleton<IPrivacySettingsUseCase, PrivacySettingsService>();
        services.AddSingleton<IDataDeletionUseCase, DataDeletionService>();
        services.AddSingleton<IAppLifecycleUseCase, AppLifecycleService>();
        services.AddSingleton<DatabaseBootstrapper>();

        services.AddSingleton<UpdaterService>();
        services.AddSingleton<UpdateViewModel>();
        services.AddSingleton<ProtectionStatusViewModel>();
        services.AddSingleton<CameraStatusViewModel>();
        services.AddSingleton<DashboardViewModel>();
        services.AddSingleton<ReportViewModel>();
        services.AddSingleton<ScheduleViewModel>();
        services.AddSingleton<BlocklistViewModel>();
        services.AddSingleton<PrivacyViewModel>();
        services.AddSingleton<MainWindowViewModel>();
        services.AddSingleton<TrayIconService>();
        services.AddSingleton<ScheduleSchedulerService>();
        services.AddSingleton<RetentionSchedulerService>();
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

    /// <summary>
    /// Runs <c>--watchdog</c>, <c>--watchdog-stop</c> and
    /// <c>--uninstall-watchdog</c> before the app mutex, onboarding or any
    /// window exists. The watchdog provider is intentionally minimal: it only
    /// needs settings, audit, the runtime folder and the policy.
    /// </summary>
    private async Task RunWatchdogCommandAsync(WatchdogCommandLine command)
    {
        var exitCode = 0;
        ServiceProvider? provider = null;
        IDisposable? correlationScope = null;

        try
        {
            provider = command.Mode == WatchdogCommandMode.Watchdog
                ? BuildWatchdogServiceProvider()
                : BuildMaintenanceServiceProvider();

            correlationScope = provider
                .GetRequiredService<ICorrelationContext>()
                .BeginScope(Guid.NewGuid());

            var logger = provider.GetRequiredService<IAppLogger>();
            logger.Info(LogCategories.Watchdog, $"Watchdog command '{command.Mode}' starting.");

            exitCode = command.Mode switch
            {
                WatchdogCommandMode.Watchdog => await RunWatchdogLoopAsync(provider, command.LoginStart),
                WatchdogCommandMode.Stop => await StopWatchdogAsync(provider, command.WaitTimeout),
                WatchdogCommandMode.Uninstall => await UninstallWatchdogAsync(provider, command.WaitTimeout),
                _ => 0
            };

            logger.Info(LogCategories.Watchdog, $"Watchdog command '{command.Mode}' finished (exit={exitCode}).");
        }
        catch (Exception ex)
        {
            exitCode = 1;

            try
            {
                provider?.GetService<IAppLogger>()?.Error(
                    LogCategories.Watchdog,
                    $"Watchdog command '{command.Mode}' failed.",
                    ex);
            }
            catch
            {
                // Logging a failed command is best effort.
            }
        }
        finally
        {
            correlationScope?.Dispose();

            if (provider is not null)
            {
                try
                {
                    await provider.DisposeAsync();
                }
                catch
                {
                    // A failed provider must not block process exit.
                }
            }

            Shutdown(exitCode);
        }
    }

    private static async Task<int> RunWatchdogLoopAsync(ServiceProvider provider, bool loginStart)
    {
        var logger = provider.GetRequiredService<IAppLogger>();

        try
        {
            // The watchdog can start before the app after an upgrade; applying
            // the schema here lets it read settings instead of failing on an
            // old database.
            await provider.GetRequiredService<SqliteDatabaseInitializer>().InitializeAsync();
        }
        catch (Exception ex)
        {
            logger.Error(
                LogCategories.Watchdog,
                "The watchdog could not prepare the database; it exits and the health task retries.",
                ex);
            return 1;
        }

        logger.Info(LogCategories.Watchdog, "====== EduFocus watchdog started ======");
        await provider.GetRequiredService<WatchdogHost>().RunAsync(loginStart);
        logger.Info(LogCategories.Watchdog, "====== EduFocus watchdog exited ======");
        return 0;
    }

    private static async Task<int> StopWatchdogAsync(ServiceProvider provider, TimeSpan waitTimeout)
    {
        var logger = provider.GetRequiredService<IAppLogger>();
        var stopped = await provider.GetRequiredService<IWatchdogRuntimePort>().RequestStopAsync(waitTimeout);

        if (stopped)
            logger.Info(LogCategories.Watchdog, "Watchdog stopped.");
        else
            logger.Warning(LogCategories.Watchdog, "Watchdog did not stop within the wait time.");

        return stopped ? 0 : 1;
    }

    private static async Task<int> UninstallWatchdogAsync(ServiceProvider provider, TimeSpan waitTimeout)
    {
        var logger = provider.GetRequiredService<IAppLogger>();

        await provider
            .GetRequiredService<IWatchdogAutoStartPort>()
            .SynchronizeAsync(WatchdogRegistrationMode.None);

        var stopped = await provider.GetRequiredService<IWatchdogRuntimePort>().RequestStopAsync(waitTimeout);

        provider.GetRequiredService<WatchdogRuntimeFiles>().DeleteAll();

        logger.Info(
            LogCategories.Watchdog,
            stopped
                ? "Watchdog uninstalled: scheduled tasks removed, process stopped and runtime files cleared."
                : "Watchdog uninstalled partially: scheduled tasks removed and runtime files cleared, but the process did not stop in time.");

        return stopped ? 0 : 1;
    }

    private static ServiceProvider BuildWatchdogServiceProvider()
    {
        var services = new ServiceCollection();
        services.AddInfrastructureServices();
        services.AddSingleton(new EduFocusDatabaseOptions());
        services.AddSingleton<SqliteDatabaseInitializer>();
        services.AddSingleton<IAppSettingsRepository, SqliteAppSettingsRepository>();
        services.AddSingleton<IAuditLogRepository, SqliteAuditLogRepository>();
        services.AddSingleton(new WatchdogOptions());
        services.AddSingleton<IWatchdogUseCase, WatchdogService>();
        services.AddSingleton<WatchdogHost>();
        return services.BuildServiceProvider();
    }

    private static ServiceProvider BuildMaintenanceServiceProvider()
    {
        var services = new ServiceCollection();
        services.AddInfrastructureServices();
        return services.BuildServiceProvider();
    }

    protected override void OnSessionEnding(SessionEndingCancelEventArgs e)
    {
        var logger = Services?.GetService<IAppLogger>();

        try
        {
            // Tell the supervisor first: it must not restart the app while
            // Windows is shutting down or signing out.
            Services?.GetService<IWatchdogRuntimePort>()?.NotifySessionEnding();
        }
        catch (Exception ex)
        {
            logger?.Error(LogCategories.Watchdog, "Notifying the watchdog about the Windows shutdown failed.", ex);
        }

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

        try
        {
            // The engine and the heartbeat only implement IAsyncDisposable, so
            // the synchronous ServiceProvider.Dispose() would throw.
            if (Services is { } services)
                services.DisposeAsync().AsTask().GetAwaiter().GetResult();
        }
        catch (Exception ex)
        {
            try
            {
                Services?.GetService<IAppLogger>()?.Error(LogCategories.App, "Service disposal failed on exit.", ex);
            }
            catch { }
        }
        finally
        {
            _correlationScope?.Dispose();
        }

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
