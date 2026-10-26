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
        services.AddSingleton<IProtectionEnginePort, UnavailableProtectionEngine>();
        return services;
    }
}
