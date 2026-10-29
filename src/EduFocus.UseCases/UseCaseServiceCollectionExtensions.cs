using Microsoft.Extensions.DependencyInjection;
using EduFocus.Ports.Inbound;
using EduFocus.Ports.Outbound.Services;
using EduFocus.UseCases.Profiles;
using EduFocus.UseCases.Settings;
using EduFocus.UseCases.Logging;

namespace EduFocus.UseCases;

public static class UseCaseServiceCollectionExtensions
{
    public static IServiceCollection AddUseCases(this IServiceCollection services)
    {
        // State engine
        services.AddSingleton<ActiveSessionState>();

        // Use Cases — real implementations registered under concrete type for decorator resolution
        services.AddScoped<IFocusTaskUseCase, FocusTaskUseCase>();
        services.AddScoped<FocusSessionUseCase>();   // concrete registration
        services.AddScoped<IFocusSessionUseCase>(sp =>
            new LoggingFocusSessionDecorator(
                sp.GetRequiredService<FocusSessionUseCase>(),
                sp.GetRequiredService<IAppLogger>()));

        services.AddScoped<IProfileUseCase, ProfileUseCase>();
        services.AddScoped<IProfileEntryUseCase, ProfileEntryUseCase>();
        services.AddScoped<IProfileSyncUseCase, ProfileSyncUseCase>();
        services.AddScoped<ISystemSettingsUseCase, SystemSettingsUseCase>();
        services.AddScoped<IQuoteUseCase, QuoteUseCase>();
        services.AddScoped<IAnalyticsUseCase, AnalyticsUseCase>();

        return services;
    }
}
