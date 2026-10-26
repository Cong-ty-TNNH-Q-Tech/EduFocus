using System.IO;
using System.Windows;
using EduFocus.Infrastructure;
using EduFocus.Infrastructure.Persistence;
using EduFocus.Ports.Outbound.Services;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace EduFocus.WPF;

public partial class App : Application
{
    private ServiceProvider? _services;
    private IServiceScope? _windowScope;

    protected override async void OnStartup(StartupEventArgs e)
    {
        base.OnStartup(e);
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
        MainWindow = new MainWindow(
            _windowScope.ServiceProvider.GetRequiredService<ParentCredentialService>(),
            _windowScope.ServiceProvider.GetRequiredService<ProtectionCoordinator>(),
            _services.GetRequiredService<IAppLogger>(),
            configured);
        MainWindow.Closed += (_, _) => _windowScope?.Dispose();
        MainWindow.Show();
        ShutdownMode = ShutdownMode.OnLastWindowClose;
    }

    protected override void OnExit(ExitEventArgs e)
    {
        _windowScope?.Dispose();
        _services?.Dispose();
        base.OnExit(e);
    }
}
