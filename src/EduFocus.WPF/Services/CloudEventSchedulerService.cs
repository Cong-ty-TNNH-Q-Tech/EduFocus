using System.Windows.Threading;
using EduFocus.Ports.Inbound;
using EduFocus.Ports.Outbound.Services;
using EduFocus.UseCases.Cloud;

namespace EduFocus.WPF.Services;

/// <summary>
/// Drives the cloud producers every 30 minutes and once at startup: the daily
/// summary while protection is on, and the tamper scan for security alerts.
/// Cloud outages surface as warnings only, so the timer must keep running and
/// never throw into the dispatcher.
/// </summary>
public sealed class CloudEventSchedulerService : IDisposable
{
    public static readonly TimeSpan Interval = TimeSpan.FromMinutes(30);

    private readonly CloudDailySummaryProducer _summaries;
    private readonly CloudSecurityAlertProducer _security;
    private readonly IProtectionUseCase _protection;
    private readonly IAppLogger _logger;
    private readonly DispatcherTimer _timer;

    private bool _publishing;

    public CloudEventSchedulerService(
        CloudDailySummaryProducer summaries,
        CloudSecurityAlertProducer security,
        IProtectionUseCase protection,
        IAppLogger logger)
    {
        _summaries = summaries ?? throw new ArgumentNullException(nameof(summaries));
        _security = security ?? throw new ArgumentNullException(nameof(security));
        _protection = protection ?? throw new ArgumentNullException(nameof(protection));
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));

        _timer = new DispatcherTimer(DispatcherPriority.Background)
        {
            Interval = Interval
        };
        _timer.Tick += OnTimerTick;
    }

    /// <summary>Captures the startup snapshot and scans tamper audit rows once.</summary>
    public Task PublishStartupAsync() =>
        PublishAsync(requireProtectionOn: false);

    public void Start() => _timer.Start();

    public void Dispose()
    {
        _timer.Stop();
        _timer.Tick -= OnTimerTick;
    }

    private async void OnTimerTick(object? sender, EventArgs e) =>
        await PublishAsync(requireProtectionOn: true);

    private async Task PublishAsync(bool requireProtectionOn)
    {
        if (_publishing)
            return;

        _publishing = true;

        try
        {
            await Task.Run(async () =>
            {
                if (!requireProtectionOn || await IsProtectionOnAsync())
                    await _summaries.TryPublishAsync();

                await _security.TryScanAsync();
            });
        }
        catch (Exception ex)
        {
            _logger.Warning(
                LogCategories.Cloud,
                $"The cloud producer tick failed ({ex.GetType().Name}).");
        }
        finally
        {
            _publishing = false;
        }
    }

    private async Task<bool> IsProtectionOnAsync()
    {
        try
        {
            return (await _protection.GetStatusAsync()).IsEnabled;
        }
        catch (Exception ex)
        {
            _logger.Warning(
                LogCategories.Cloud,
                $"Could not read the protection state for the cloud summary ({ex.GetType().Name}).");
            return false;
        }
    }
}
