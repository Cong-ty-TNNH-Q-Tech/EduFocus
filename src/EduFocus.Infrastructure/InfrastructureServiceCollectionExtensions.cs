using System.Runtime.Versioning;
using EduFocus.Infrastructure.Auth;
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

        return services;
    }
}
