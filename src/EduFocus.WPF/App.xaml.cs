using System.Windows;
using EduFocus.Infrastructure;
using EduFocus.Ports.Outbound.Services;
using Microsoft.Extensions.DependencyInjection;

namespace EduFocus.WPF;

public partial class App : Application
{
    private ServiceProvider? _services;

    protected override void OnStartup(StartupEventArgs e)
    {
        base.OnStartup(e);

        var services = new ServiceCollection();
        services.AddInfrastructureServices();
        _services = services.BuildServiceProvider();
        _services.GetRequiredService<IAppLogger>().Info("EduFocus foundation started.");

        MainWindow = new MainWindow();
        MainWindow.Show();
    }

    protected override void OnExit(ExitEventArgs e)
    {
        _services?.Dispose();
        base.OnExit(e);
    }
}