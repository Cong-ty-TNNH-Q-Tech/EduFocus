using EduFocus.Cloud.Api.Options;
using Microsoft.Extensions.Options;

namespace EduFocus.Cloud.Api.Jobs;

public sealed class CloudMaintenanceWorker(
    IServiceScopeFactory scopeFactory,
    IOptions<JobOptions> options,
    ILogger<CloudMaintenanceWorker> logger) : BackgroundService
{
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        var settings = options.Value;
        if (!settings.Enabled)
        {
            logger.LogInformation("Cloud maintenance jobs are disabled; worker will not run.");
            return;
        }

        logger.LogInformation(
            "Cloud maintenance worker started with a {IntervalSeconds}s interval.",
            settings.IntervalSeconds);

        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                await using var scope = scopeFactory.CreateAsyncScope();
                var service = scope.ServiceProvider.GetRequiredService<CloudMaintenanceService>();
                await service.RunAsync(stoppingToken);
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
            {
                break;
            }
            catch (Exception exception)
            {
                logger.LogError(
                    "Cloud maintenance iteration failed: {ErrorType}",
                    exception.GetType().Name);
            }

            try
            {
                await Task.Delay(TimeSpan.FromSeconds(settings.IntervalSeconds), stoppingToken);
            }
            catch (OperationCanceledException)
            {
                break;
            }
        }
    }
}
