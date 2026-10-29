using System.Runtime.Versioning;
using EduFocus.Infrastructure.Logging;
using EduFocus.Infrastructure.Services;
using EduFocus.Ports.Outbound.Services;
using Microsoft.Extensions.DependencyInjection;

namespace EduFocus.Infrastructure;

public static class InfrastructureServiceCollectionExtensions
{
    [SupportedOSPlatform("windows")]
    public static IServiceCollection AddInfrastructureServices(this IServiceCollection services)
    {
        services.AddSingleton<IAppLogger, FileLogger>();
        services.AddSingleton<IMonotonicClockPort, MonotonicClockAdapter>();

        return services;
    }
}
