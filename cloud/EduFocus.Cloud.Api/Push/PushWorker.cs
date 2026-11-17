using EduFocus.Cloud.Api.Options;
using Microsoft.Extensions.Options;

namespace EduFocus.Cloud.Api.Push;

public sealed class PushWorker(
    IServiceScopeFactory scopeFactory,
    IOptions<PushOptions> options,
    ILogger<PushWorker> logger) : BackgroundService
{
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        var settings = options.Value;
        if (!settings.Enabled)
        {
            logger.LogInformation("Push is disabled; dispatch worker will not run.");
            return;
        }

        logger.LogInformation(
            "Push dispatch worker started with a {PollSeconds}s interval.",
            settings.PollSeconds);

        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                await using var scope = scopeFactory.CreateAsyncScope();
                var dispatcher = scope.ServiceProvider.GetRequiredService<PushDispatchService>();
                var processed = await dispatcher.DispatchPendingAsync(stoppingToken);
                if (processed > 0)
                {
                    logger.LogInformation("Processed {Count} pending notifications.", processed);
                }
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
            {
                break;
            }
            catch (Exception exception)
            {
                logger.LogError(
                    "Push dispatch iteration failed: {ErrorType}",
                    exception.GetType().Name);
            }

            try
            {
                await Task.Delay(TimeSpan.FromSeconds(settings.PollSeconds), stoppingToken);
            }
            catch (OperationCanceledException)
            {
                break;
            }
        }
    }
}
