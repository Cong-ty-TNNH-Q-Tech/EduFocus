using EduFocus.Domain.Protection;
using EduFocus.Infrastructure;
using EduFocus.Infrastructure.Logging;
using EduFocus.Infrastructure.ServiceControl;
using EduFocus.Ports.Inbound;
using EduFocus.Ports.Outbound.Persistence;
using EduFocus.Ports.Outbound.Services;
using EduFocus.Ports.ServiceControl;
using EduFocus.Service.Control;
using EduFocus.Service.Hosting;
using EduFocus.Service.NoOps;
using EduFocus.Service.Runtime;
using EduFocus.SqliteDataAccess.Persistence;
using EduFocus.SqliteDataAccess.Repositories;
using EduFocus.UseCases.Blocking;
using EduFocus.UseCases.Protection;
using EduFocus.UseCases.Retention;
using EduFocus.UseCases.Schedule;
using EduFocus.UseCases.Sessions;
using Microsoft.Extensions.DependencyInjection;

namespace EduFocus.Service.Composition;

/// <summary>
/// Service-process composition. It reuses every infrastructure adapter but
/// swaps the interactive-only monitoring ports for no-ops and owns the
/// protection state machine, heartbeat, schedule and retention. Logs go to
/// ProgramData because SYSTEM has no local-app-data profile.
/// </summary>
public static class ServiceComposition
{
    public static ServiceProvider Build(ServicePowerNotifier powerNotifier)
    {
        ArgumentNullException.ThrowIfNull(powerNotifier);

        var services = new ServiceCollection();

        services.AddInfrastructureServices();
        services.AddSingleton(
            new FileLoggerOptions
            {
                DirectoryPath = Path.Combine(EduFocusDatabaseOptions.DefaultDirectory, "logs")
            });

        services.AddSingleton(new EduFocusDatabaseOptions());
        services.AddSingleton<IDatabaseKeyStore, DatabaseKeyStore>();
        services.AddSingleton<SqliteDbContextFactory>();
        services.AddSingleton<SqliteDatabaseInitializer>();
        services.AddSingleton<IAppSettingsRepository, SqliteAppSettingsRepository>();
        services.AddSingleton<IParentCredentialRepository, SqliteParentCredentialRepository>();
        services.AddSingleton<IAuditLogRepository, SqliteAuditLogRepository>();
        services.AddSingleton<IStudySessionRepository, SqliteStudySessionRepository>();
        services.AddSingleton<IAttentionEventRepository, SqliteAttentionEventRepository>();
        services.AddSingleton<IScheduleWindowRepository, SqliteScheduleWindowRepository>();
        services.AddSingleton<IBlockRuleRepository, SqliteBlockRuleRepository>();
        services.AddSingleton<IBlockedAttemptRepository, SqliteBlockedAttemptRepository>();
        services.AddSingleton<IRetentionRepository, SqliteRetentionRepository>();

        services.AddSingleton<ISystemPowerEventPort>(powerNotifier);
        services.AddSingleton(new StudySessionHeartbeatOptions());
        services.AddSingleton<IStudySessionHeartbeatUseCase, StudySessionHeartbeatService>();
        services.AddSingleton<IActivityUseCase, NullActivityUseCase>();
        services.AddSingleton<IAttentionPipelineUseCase, NullAttentionPipelineUseCase>();
        services.AddSingleton<IAttentionMonitorUseCase, NullAttentionMonitorUseCase>();

        services.AddSingleton(BuiltInRuleCatalog.Default);
        services.AddSingleton<IBlockRuleSeedUseCase, BlockRuleSeedService>();
        services.AddSingleton<ServiceRequestAuthorizer>();
        services.AddSingleton<IParentAuthorizer>(provider => provider.GetRequiredService<ServiceRequestAuthorizer>());
        services.AddSingleton<ProtectionService>();
        services.AddSingleton<IProtectionUseCase>(provider => provider.GetRequiredService<ProtectionService>());
        services.AddSingleton<IScheduleUseCase, ScheduleService>();
        services.AddSingleton(new RetentionOptions());
        services.AddSingleton<IRetentionUseCase, RetentionService>();

        services.AddSingleton<ServiceRuntimeStatus>();
        services.AddSingleton(new ServiceRuntimeOptions());
        services.AddSingleton(new ServiceControlServerOptions
        {
            ExpectedClientImagePath = Path.Combine(
                AppContext.BaseDirectory,
                ServiceIdentity.UiExecutableName)
        });
        services.AddSingleton<IPipeClientVerifier>(provider =>
        {
            var options = provider.GetRequiredService<ServiceControlServerOptions>();
            return new ExpectedProcessPipeClientVerifier(options.ExpectedClientImagePath!);
        });
        services.AddSingleton<IServiceControlHandler, ServiceCommandHandler>();
        services.AddSingleton<ServiceControlServer>();
        services.AddSingleton<ServiceRuntime>();

        return services.BuildServiceProvider(new ServiceProviderOptions
        {
            ValidateOnBuild = false,
            ValidateScopes = false
        });
    }
}
