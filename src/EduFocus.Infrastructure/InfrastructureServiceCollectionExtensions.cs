using System.Runtime.Versioning;
using EduFocus.Infrastructure.Activity;
using EduFocus.Infrastructure.Auth;
using EduFocus.Infrastructure.Export;
using EduFocus.Infrastructure.Logging;
using EduFocus.Infrastructure.Protection;
using EduFocus.Infrastructure.Services;
using EduFocus.Infrastructure.Watchdog;
using EduFocus.Ports.Outbound.Services;
using Microsoft.Extensions.DependencyInjection;

namespace EduFocus.Infrastructure;

public static class InfrastructureServiceCollectionExtensions
{
    [SupportedOSPlatform("windows")]
    public static IServiceCollection AddInfrastructureServices(this IServiceCollection services)
    {
        services.AddSingleton(TimeProvider.System);
        services.AddSingleton<ICorrelationContext, CorrelationContext>();
        services.AddSingleton<FileLoggerOptions>();
        services.AddSingleton<IAppLogger>(provider => new FileLogger(
            provider.GetRequiredService<FileLoggerOptions>(),
            provider.GetRequiredService<ICorrelationContext>(),
            provider.GetRequiredService<TimeProvider>()));
        services.AddSingleton<IMonotonicClockPort, MonotonicClockAdapter>();
        services.AddSingleton<IPasswordHasher, Pbkdf2PasswordHasher>();
        services.AddSingleton<IRecoveryCodeGenerator, RecoveryCodeGenerator>();
        services.AddSingleton(new HostsFileOptions());
        services.AddSingleton<IDnsFlusher, IpconfigDnsFlusher>();
        services.AddSingleton<IHostsFileBlocker, HostsFileBlocker>();
        services.AddSingleton(new BlockPageOptions());
        services.AddSingleton<IBlockPageServer, BlockPageServer>();
        services.AddSingleton(new ProcessWatcherOptions());
        services.AddSingleton<IProcessController, WindowsProcessController>();
        services.AddSingleton<IProcessWatcher, ProcessWatcher>();
        services.AddSingleton(new ProtectionEngineOptions());
        services.AddSingleton(new BrowserExtensionBridgeOptions());
        services.AddSingleton<IBrowserExtensionBridge, NamedPipeBrowserExtensionBridge>();
        services.AddSingleton<IProtectionEnginePort, ProtectionEngine>();
        services.AddSingleton<IForegroundActivityPort, WindowsForegroundActivityPort>();
        services.AddSingleton(new CsvExportOptions());
        services.AddSingleton<ICsvExportStore, LocalCsvExportStore>();
        services.AddSingleton(new WatchdogRuntimeOptions());
        services.AddSingleton(new WatchdogHeartbeatOptions());
        services.AddSingleton<WatchdogRuntimeFiles>();
        services.AddSingleton<IWatchdogHeartbeatStore, JsonWatchdogHeartbeatStore>();
        services.AddSingleton<IWatchdogStateStore, JsonWatchdogStateStore>();
        services.AddSingleton<IWatchdogProcessPort, WindowsWatchdogProcessController>();
        services.AddSingleton<IWatchdogRuntimePort, WindowsWatchdogRuntime>();
        services.AddSingleton<IWatchdogCommandRunner, ProcessWatchdogCommandRunner>();
        services.AddSingleton<IProcessElevationProbe, WindowsProcessElevationProbe>();
        services.AddSingleton<IWatchdogAutoStartPort, WindowsWatchdogAutoStart>();
        services.AddSingleton<WatchdogHeartbeatPublisher>();

        return services;
    }
}
