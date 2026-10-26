using EduFocus.Infrastructure.Logging;
using EduFocus.Infrastructure.Services;
using EduFocus.Ports.Outbound.Services;
using Microsoft.Extensions.DependencyInjection;

namespace EduFocus.Infrastructure;

public static class InfrastructureServiceCollectionExtensions
{
    public static IServiceCollection AddInfrastructureServices(this IServiceCollection services)
    {
        services.AddSingleton<IAppLogger, FileLogger>();
        services.AddSingleton<IMonotonicClockPort, MonotonicClockAdapter>();
        services.AddSingleton<IProtectionEnginePort, ProtectionEngine>();
        services.AddSingleton<IHostsFileStorage, PhysicalHostsFileStorage>();
        services.AddSingleton<IDnsCacheFlusher, WindowsDnsCacheFlusher>();
        services.AddSingleton<IBlockPagePort>(provider =>
            new LoopbackBlockPagePort(provider.GetRequiredService<IAppLogger>(),
                scopeFactory: provider.GetRequiredService<IServiceScopeFactory>()));
        services.AddSingleton<IProcessSnapshotProvider, SystemProcessSnapshotProvider>();
        services.AddSingleton<IProcessWatcherPort>(provider => new ProcessWatcher(
            provider.GetRequiredService<IProcessSnapshotProvider>(),
            provider.GetRequiredService<IServiceScopeFactory>(),
            provider.GetRequiredService<IAppLogger>()));
        services.AddSingleton<IHostsFilePort>(provider =>
        {
            var windows = Environment.GetFolderPath(Environment.SpecialFolder.Windows);
            var commonData = Environment.GetFolderPath(Environment.SpecialFolder.CommonApplicationData);
            if (string.IsNullOrWhiteSpace(windows) || string.IsNullOrWhiteSpace(commonData))
                throw new InvalidOperationException("Windows system and common application data paths are required for hosts protection.");
            return new WindowsHostsFilePort(
                Path.Combine(windows, "System32", "drivers", "etc", "hosts"),
                Path.Combine(commonData, "EduFocus", "hosts.original.backup"),
                provider.GetRequiredService<IHostsFileStorage>(),
                provider.GetRequiredService<IDnsCacheFlusher>(),
                provider.GetRequiredService<IAppLogger>());
        });
        return services;
    }
}
