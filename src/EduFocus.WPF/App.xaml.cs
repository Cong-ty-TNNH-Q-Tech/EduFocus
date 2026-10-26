using System.IO;
using System.Windows;
using EduFocus.Infrastructure;
using EduFocus.Infrastructure.Persistence;
using EduFocus.Ports.Outbound.Services;
using Microsoft.Extensions.DependencyInjection;

namespace EduFocus.WPF;

public partial class App : Application
{
    private ServiceProvider? _services;

    protected override async void OnStartup(StartupEventArgs e)
    {
        base.OnStartup(e);

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
            _services.GetRequiredService<IAppLogger>().Info("EduFocus foundation started.");
            await _services.GetRequiredService<DatabaseInitializer>().InitializeAsync(databasePath);
        }
        catch (Exception)
        {
            MessageBox.Show("EduFocus could not open or initialize its private database. It did not automatically delete or replace your data. Check the app log and follow docs/persistence-recovery.md before restoring a backup.", "EduFocus database error", MessageBoxButton.OK, MessageBoxImage.Error);
            _services.Dispose();
            _services = null;
            Shutdown(-1);
            return;
        }

        MainWindow = new MainWindow();
        MainWindow.Show();
    }

    protected override void OnExit(ExitEventArgs e)
    {
        _services?.Dispose();
        base.OnExit(e);
    }
}
