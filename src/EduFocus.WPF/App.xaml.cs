using System.IO;
using System.Threading.Tasks;
using System.Windows;
using EduFocus.Infrastructure;
using EduFocus.Infrastructure.Persistence;
using EduFocus.Ports.Outbound.Services;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Win32;

namespace EduFocus.WPF;

public partial class App : Application
{
    private ServiceProvider? _services;
    private IServiceScope? _windowScope;
    private CancellationTokenSource? _heartbeatStop;
    private Task? _heartbeatTask;
    private Task? _activityTask;
    private Task? _retentionTask;
    private volatile bool _sessionLocked;

    protected override async void OnStartup(StartupEventArgs e)
    {
        base.OnStartup(e);
        SessionEnding += OnSystemSessionEnding;
        ShutdownMode = ShutdownMode.OnExplicitShutdown;

        var databasePath = DatabaseInitializer.GetDatabasePath();
        try
        {
            var dataDirectory = Path.GetDirectoryName(databasePath)!;
            new WindowsDataDirectoryProtector().Protect(dataDirectory);
        }
        catch (Exception)
        {
            MessageBox.Show("EduFocus could not secure its private data folder. Check that you started the app with an administrator account. No data was deleted.", "EduFocus data access", MessageBoxButton.OK, MessageBoxImage.Error);
            Shutdown(-1);
            return;
        }

        var services = new ServiceCollection();
        services.AddInfrastructureServices();
        services.AddEduFocusPersistence(databasePath);
        _services = services.BuildServiceProvider();
        SystemEvents.PowerModeChanged += OnPowerModeChanged;
        SystemEvents.SessionSwitch += OnSessionSwitch;

        try
        {
            var logger = _services.GetRequiredService<IAppLogger>();
            using (logger.BeginCorrelationScope())
            {
                logger.Log("Information", "EduFocus.WPF.App", "EduFocus foundation started.");
                await _services.GetRequiredService<DatabaseInitializer>().InitializeAsync(databasePath);
            }
        }
        catch (Exception)
        {
            MessageBox.Show("EduFocus could not open or initialize its private database. It did not automatically delete or replace your data. Check the app log and follow docs/persistence-recovery.md before restoring a backup.", "EduFocus database error", MessageBoxButton.OK, MessageBoxImage.Error);
            _services.Dispose();
            _services = null;
            Shutdown(-1);
            return;
        }

        bool configured;
        try
        {
            _windowScope = _services.CreateScope();
            await _windowScope.ServiceProvider.GetRequiredService<DefaultBlockRuleSeeder>()
                .SeedAsync(DateTimeOffset.UtcNow);
            var db = _windowScope.ServiceProvider.GetRequiredService<AppDbContext>();
            var onboarding = _windowScope.ServiceProvider.GetRequiredService<OnboardingService>();
            var progress = await onboarding.GetProgressAsync();
            if (!progress.Completed)
            {
                var wizard = new OnboardingWindow(onboarding,
                    _windowScope.ServiceProvider.GetRequiredService<ParentCredentialService>(),
                    _services.GetRequiredService<IAppLogger>());
                if (wizard.ShowDialog() != true)
                {
                    _windowScope.Dispose();
                    _windowScope = null;
                    Shutdown(-1);
                    return;
                }
            }
            await _windowScope.ServiceProvider.GetRequiredService<ProtectionCoordinator>()
                .ReconcileAfterRestartAsync(DateTimeOffset.UtcNow);
            configured = await db.ParentCredentials.AnyAsync();
        }
        catch (Exception ex)
        {
            _services.GetRequiredService<IAppLogger>().Error("EduFocus could not safely restore its startup state.", ex);
            _windowScope?.Dispose();
            _windowScope = null;
            _services.Dispose();
            _services = null;
            MessageBox.Show("EduFocus could not safely restore its startup state. No protected controls were opened; no data was deleted.", "EduFocus startup error", MessageBoxButton.OK, MessageBoxImage.Error);
            Shutdown(-1);
            return;
        }
        _heartbeatStop = new CancellationTokenSource();
        _heartbeatTask = RunSessionHeartbeatAsync(_services, _heartbeatStop.Token);
        _activityTask = RunActivityTrackingAsync(_services, _heartbeatStop.Token);
        _retentionTask = RunRetentionSchedulerAsync(_services, _heartbeatStop.Token);
        MainWindow = new MainWindow(
            _windowScope.ServiceProvider.GetRequiredService<ParentCredentialService>(),
            _windowScope.ServiceProvider.GetRequiredService<ProtectionCoordinator>(),
            _windowScope.ServiceProvider.GetRequiredService<ScheduleService>(),
            _windowScope.ServiceProvider.GetRequiredService<BlockRuleService>(),
            _services.GetRequiredService<IAppLogger>(),
            configured);
        MainWindow.Closed += (_, _) => _windowScope?.Dispose();
        MainWindow.Show();
        ShutdownMode = ShutdownMode.OnLastWindowClose;
    }

    private void OnSystemSessionEnding(object? sender, SessionEndingCancelEventArgs e)
    {
        if (_windowScope is null) return;
        try
        {
            var scope = _windowScope;
            Task.Run(() => scope.ServiceProvider.GetRequiredService<ProtectionCoordinator>()
                .HandleSystemShutdownAsync(DateTimeOffset.UtcNow)).GetAwaiter().GetResult();
        }
        catch (Exception ex)
        {
            _services?.GetRequiredService<IAppLogger>().Error("System shutdown state could not be recorded; Windows shutdown will continue.", ex);
        }
        (MainWindow as MainWindow)?.AllowSystemShutdown();
        // Do not cancel the OS shutdown or logoff request.
    }

    protected override void OnExit(ExitEventArgs e)
    {
        SystemEvents.PowerModeChanged -= OnPowerModeChanged;
        SystemEvents.SessionSwitch -= OnSessionSwitch;
        _heartbeatStop?.Cancel();
        try { Task.WhenAll(_heartbeatTask ?? Task.CompletedTask, _activityTask ?? Task.CompletedTask,
            _retentionTask ?? Task.CompletedTask).GetAwaiter().GetResult(); }
        catch (OperationCanceledException) { }
        _heartbeatStop?.Dispose();
        _windowScope?.Dispose();
        _services?.Dispose();
        base.OnExit(e);
    }

    private async Task RunSessionHeartbeatAsync(IServiceProvider services, CancellationToken cancellationToken)
    {
        using var timer = new PeriodicTimer(TimeSpan.FromSeconds(10));
        while (await timer.WaitForNextTickAsync(cancellationToken))
        {
            if (_sessionLocked) continue;
            try
            {
                using var scope = services.CreateScope();
                await scope.ServiceProvider.GetRequiredService<ProtectionCoordinator>()
                    .HeartbeatAsync(DateTimeOffset.UtcNow, cancellationToken);
            }
            catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested) { throw; }
            catch (Exception ex)
            {
                services.GetRequiredService<IAppLogger>().Error("Study session heartbeat could not be saved.", ex);
            }
        }
    }

    private async Task RunActivityTrackingAsync(IServiceProvider services, CancellationToken cancellationToken)
    {
        var tracker = services.GetRequiredService<ActivityTrackingService>();
        using var timer = new PeriodicTimer(TimeSpan.FromSeconds(1));
        while (await timer.WaitForNextTickAsync(cancellationToken))
        {
            if (_sessionLocked) continue;
            try { await tracker.ScanOnceAsync(DateTimeOffset.UtcNow, cancellationToken); }
            catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested) { throw; }
            catch (Exception ex) { services.GetRequiredService<IAppLogger>().Error("Activity sample could not be saved.", ex); }
        }
    }

    private static async Task RunRetentionSchedulerAsync(IServiceProvider services, CancellationToken cancellationToken)
    {
        var startupSucceeded = await RunRetentionOnceAsync(services, cancellationToken);
        var localNow = TimeZoneInfo.ConvertTime(DateTimeOffset.UtcNow, TimeZoneInfo.Local).DateTime;
        var lastScheduledDate = startupSucceeded && localNow.TimeOfDay >= TimeSpan.FromHours(3)
            ? DateOnly.FromDateTime(localNow)
            : DateOnly.FromDateTime(localNow).AddDays(-1);
        using var timer = new PeriodicTimer(TimeSpan.FromSeconds(30));
        while (await timer.WaitForNextTickAsync(cancellationToken))
        {
            var local = TimeZoneInfo.ConvertTime(DateTimeOffset.UtcNow, TimeZoneInfo.Local).DateTime;
            var localDate = DateOnly.FromDateTime(local);
            if (local.TimeOfDay < TimeSpan.FromHours(3) || localDate <= lastScheduledDate) continue;
            if (await RunRetentionOnceAsync(services, cancellationToken)) lastScheduledDate = localDate;
        }
    }

    private static async Task<bool> RunRetentionOnceAsync(IServiceProvider services, CancellationToken cancellationToken)
    {
        try
        {
            using var scope = services.CreateScope();
            await scope.ServiceProvider.GetRequiredService<RetentionService>()
                .RunAsync(DateTimeOffset.UtcNow, cancellationToken);
            return true;
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested) { throw; }
        catch (Exception ex)
        {
            services.GetRequiredService<IAppLogger>().Error("Retention cleanup did not complete; it will retry safely.", ex);
            return false;
        }
    }

    private void OnPowerModeChanged(object? sender, PowerModeChangedEventArgs e)
    {
        if (_services is null) return;
        try
        {
            using var scope = _services.CreateScope();
            var coordinator = scope.ServiceProvider.GetRequiredService<ProtectionCoordinator>();
            if (e.Mode == PowerModes.Suspend && !_sessionLocked)
                coordinator.HeartbeatAsync(DateTimeOffset.UtcNow).GetAwaiter().GetResult();
            else if (e.Mode == PowerModes.Resume)
                ResynchronizeAfterResumeAsync(scope.ServiceProvider, DateTimeOffset.UtcNow).GetAwaiter().GetResult();
        }
        catch (Exception ex) { _services.GetRequiredService<IAppLogger>().Error("Protection session could not be synchronized with a power state change.", ex); }
    }

    private void OnSessionSwitch(object? sender, SessionSwitchEventArgs e)
    {
        if (_services is null) return;
        try
        {
            using var scope = _services.CreateScope();
            var coordinator = scope.ServiceProvider.GetRequiredService<ProtectionCoordinator>();
            if (e.Reason == SessionSwitchReason.SessionLock)
            {
                _sessionLocked = true;
                coordinator.HeartbeatAsync(DateTimeOffset.UtcNow).GetAwaiter().GetResult();
            }
            else if (e.Reason == SessionSwitchReason.SessionUnlock)
            {
                ResynchronizeAfterResumeAsync(scope.ServiceProvider, DateTimeOffset.UtcNow).GetAwaiter().GetResult();
                _sessionLocked = false;
            }
        }
        catch (Exception ex) { _services.GetRequiredService<IAppLogger>().Error("Protection session could not be synchronized with a Windows session change.", ex); }
    }

    private static async Task ResynchronizeAfterResumeAsync(IServiceProvider services, DateTimeOffset now)
    {
        await services.GetRequiredService<ProtectionCoordinator>().ResynchronizeHeartbeatAsync(now);
        await services.GetRequiredService<ActivityTrackingService>().ResetAfterResumeAsync(now);
        services.GetRequiredService<IForegroundActivityPort>().OnResume();
    }
}
