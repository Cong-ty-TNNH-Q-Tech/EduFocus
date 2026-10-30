using System.IO;
using System.Windows;
using EduFocus.Domain.Attention;
using EduFocus.Domain.Protection;
using EduFocus.Infrastructure;
using EduFocus.Infrastructure.Attention;
using EduFocus.Infrastructure.Attention.Camera;
using EduFocus.Infrastructure.ServiceControl;
using EduFocus.Infrastructure.Watchdog;
using EduFocus.Ports.DTOs;
using EduFocus.Ports.Inbound;
using EduFocus.Ports.Outbound.Persistence;
using EduFocus.Ports.Outbound.Services;
using EduFocus.Ports.ServiceControl;
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

        var serviceManaged = ServiceInstallationProbe.IsServiceManaged();
        var serviceCollection = new ServiceCollection();
        ConfigureServices(serviceCollection, serviceManaged);
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

        if (serviceManaged)
            await StartServiceManagedAsync(logger);
        else
            await StartStandaloneAsync(logger);

        var mainWindow = Services.GetRequiredService<MainWindow>();
        MainWindow = mainWindow;
        ShutdownMode = ShutdownMode.OnMainWindowClose;
        mainWindow.Show();

        Services.GetRequiredService<TrayIconService>().Start();

        if (!serviceManaged)
        {
            Services.GetRequiredService<ScheduleSchedulerService>().Start();
            Services.GetRequiredService<RetentionSchedulerService>().Start();
        }
    }

    /// <summary>
    /// Standalone composition: this process owns protection, the schedule and
    /// the watchdog. The order keeps the persisted ON state recoverable: seed
    /// rules, reconcile the engine, evaluate the schedule, then synchronize the
    /// supervisor before any window shows.
    /// </summary>
    private static async Task StartStandaloneAsync(IAppLogger logger)
    {
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
        await Services.GetRequiredService<ScheduleSchedulerService>().EvaluateNowAsync();

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
    }

    /// <summary>
    /// Service composition: the Windows service owns the engine, heartbeat,
    /// session, schedule and retention. This process only projects the remote
    /// status and runs the session-bound camera/activity overlay, so it removes
    /// watchdog artifacts from older installs and never starts a local engine.
    /// </summary>
    private static async Task StartServiceManagedAsync(IAppLogger logger)
    {
        try
        {
            await Services
                .GetRequiredService<IWatchdogAutoStartPort>()
                .SynchronizeAsync(WatchdogRegistrationMode.None);
        }
        catch (Exception ex)
        {
            logger.Error(
                LogCategories.Watchdog,
                "Could not remove legacy watchdog scheduled tasks in service mode.",
                ex);
        }

        try
        {
            if (!await Services
                    .GetRequiredService<IWatchdogRuntimePort>()
                    .RequestStopAsync(TimeSpan.FromSeconds(5)))
            {
                logger.Warning(
                    LogCategories.Watchdog,
                    "A legacy watchdog process did not stop within the wait time in service mode.");
            }
        }
        catch (Exception ex)
        {
            logger.Error(
                LogCategories.Watchdog,
                "Could not stop the legacy watchdog process in service mode.",
                ex);
        }

        var poller = Services.GetRequiredService<ServiceStatusPoller>();
        await poller.RefreshOnceAsync();
        poller.Start();
    }

    private static void ConfigureServices(IServiceCollection services, bool serviceManaged)
    {
        services.AddInfrastructureServices();
        services.AddSingleton(new ProtectionHostModeOptions { ServiceManaged = serviceManaged });

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

        if (serviceManaged)
        {
            // The service is the only protection owner. The client verifies the
            // pipe server image and requires a local Parent Mode session before
            // it sends a parent command.
            services.AddSingleton(new ServiceControlClientOptions
            {
                ExpectedServerImagePath = Path.Combine(
                    AppContext.BaseDirectory,
                    ServiceIdentity.ServiceExecutableName)
            });
            services.AddSingleton(provider => new ServiceControlClient(
                provider.GetRequiredService<ServiceControlClientOptions>(),
                provider.GetRequiredService<IParentAuthorizer>()));
            services.AddSingleton<IServiceControlClient>(provider =>
                provider.GetRequiredService<ServiceControlClient>());
            services.AddSingleton<ServiceMonitoringCoordinator>();
            services.AddSingleton<ServiceStatusPoller>();
            services.AddSingleton<IProtectionUseCase>(provider =>
                new ServiceMonitoredProtectionUseCase(
                    provider.GetRequiredService<ServiceControlClient>(),
                    provider.GetRequiredService<ServiceMonitoringCoordinator>()));
        }
        else
        {
            services.AddSingleton<ProtectionService>();
            services.AddSingleton<IProtectionUseCase>(provider =>
                new WatchdogAwareProtectionService(
                    provider.GetRequiredService<ProtectionService>(),
                    provider.GetRequiredService<IWatchdogCoordinator>()));
        }
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
    /// Runs <c>--watchdog</c>, <c>--watchdog-stop</c>,
    /// <c>--uninstall-watchdog</c> and <c>--prepare-data-directory</c> before
    /// the app mutex, onboarding or any window exists. The minimal provider
    /// only needs settings, audit, the runtime folder and the policy.
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
                WatchdogCommandMode.Uninstall => await UninstallWatchdogAsync(
                    provider,
                    command.WaitTimeout,
                    command.DeleteUserData),
                WatchdogCommandMode.PrepareDataDirectory => PrepareDataDirectory(provider),
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

    /// <summary>
    /// Removes scheduled tasks, stops the watchdog, clears the runtime folder,
    /// restores the hosts file and — only when explicitly requested — deletes
    /// the user data directory. Every step is best effort so a failure never
    /// blocks the uninstall, but the exit code reports partial cleanup.
    /// </summary>
    private static async Task<int> UninstallWatchdogAsync(
        ServiceProvider provider,
        TimeSpan waitTimeout,
        bool deleteUserData)
    {
        var logger = provider.GetRequiredService<IAppLogger>();
        var exitCode = 0;

        await provider
            .GetRequiredService<IWatchdogAutoStartPort>()
            .SynchronizeAsync(WatchdogRegistrationMode.None);

        var stopped = await provider.GetRequiredService<IWatchdogRuntimePort>().RequestStopAsync(waitTimeout);
        if (!stopped)
        {
            exitCode = 1;
            logger.Warning(
                LogCategories.Watchdog,
                "The watchdog process did not stop within the wait time during uninstall.");
        }

        var runtimeFiles = provider.GetRequiredService<WatchdogRuntimeFiles>();
        runtimeFiles.DeleteAll();
        exitCode |= TryDeleteDirectory(runtimeFiles.DirectoryPath, "runtime", logger);

        try
        {
            var recovery = await provider.GetRequiredService<IHostsFileBlocker>().SelfHealAsync();
            if (recovery.Performed)
                logger.Info(LogCategories.Protection, "Uninstall removed the EduFocus section from the hosts file.");
        }
        catch (Exception ex)
        {
            exitCode = 1;
            logger.Error(LogCategories.Protection, "Uninstall could not restore the hosts file.", ex);
        }

        exitCode |= TryDeleteDirectory(
            provider.GetRequiredService<EduFocus.Infrastructure.Protection.HostsFileOptions>().BackupDirectory,
            "hosts backup",
            logger);

        if (deleteUserData && !stopped)
        {
            logger.Error(
                LogCategories.Database,
                "User data was kept because the watchdog did not stop in time during uninstall.");
            return 1;
        }

        if (deleteUserData)
            exitCode |= DeleteUserDataDirectory(logger);

        logger.Info(
            LogCategories.Watchdog,
            deleteUserData
                ? "Watchdog uninstalled and the EduFocus data directory was deleted as requested."
                : "Watchdog uninstalled: scheduled tasks removed, process stopped, runtime files and hosts backup cleared. User data kept.");

        return exitCode;
    }

    /// <summary>
    /// Installer step that creates <c>%ProgramData%\EduFocus</c> with the
    /// Administrators/SYSTEM-only ACL before the first database is written.
    /// </summary>
    private static int PrepareDataDirectory(ServiceProvider provider)
    {
        var logger = provider.GetRequiredService<IAppLogger>();
        var directory = DataDirectorySecurity.EnsureSecure(EduFocusDatabaseOptions.DefaultDirectory);

        logger.Info(
            LogCategories.Database,
            $"Installer prepared the data directory with the restricted ACL: '{directory}'.");
        return 0;
    }

    private static int DeleteUserDataDirectory(IAppLogger logger)
    {
        var directory = Path.GetFullPath(EduFocusDatabaseOptions.DefaultDirectory);
        var programData = Path.GetFullPath(
            Environment.GetFolderPath(Environment.SpecialFolder.CommonApplicationData));

        var isExpected =
            string.Equals(Path.GetFileName(directory), "EduFocus", StringComparison.OrdinalIgnoreCase)
            && string.Equals(
                Path.GetDirectoryName(directory)?.TrimEnd(Path.DirectorySeparatorChar),
                programData.TrimEnd(Path.DirectorySeparatorChar),
                StringComparison.OrdinalIgnoreCase);

        if (!isExpected)
        {
            logger.Error(
                LogCategories.Database,
                $"Refusing to delete '{directory}' because it is not the EduFocus data directory.");
            return 1;
        }

        try
        {
            if (!Directory.Exists(directory))
                return 0;

            Directory.Delete(directory, recursive: true);
            logger.Info(
                LogCategories.Database,
                $"User data deleted from '{directory}' as requested during uninstall.");
            return 0;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            logger.Error(
                LogCategories.Database,
                $"Uninstall could not delete the user data directory '{directory}'.",
                ex);
            return 1;
        }
    }

    private static int TryDeleteDirectory(string directory, string label, IAppLogger logger)
    {
        try
        {
            if (!Directory.Exists(directory))
                return 0;

            var dataRoot = Path.GetFullPath(EduFocusDatabaseOptions.DefaultDirectory)
                .TrimEnd(Path.DirectorySeparatorChar) + Path.DirectorySeparatorChar;
            var fullPath = Path.GetFullPath(directory)
                .TrimEnd(Path.DirectorySeparatorChar) + Path.DirectorySeparatorChar;

            if (!fullPath.StartsWith(dataRoot, StringComparison.OrdinalIgnoreCase))
            {
                logger.Warning(
                    LogCategories.Database,
                    $"Refusing to delete the {label} folder outside the EduFocus data directory.");
                return 1;
            }

            Directory.Delete(fullPath.TrimEnd(Path.DirectorySeparatorChar), recursive: true);
            return 0;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            logger.Warning(
                LogCategories.Database,
                $"Uninstall could not delete the {label} folder ({ex.GetType().Name}).");
            return 1;
        }
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
