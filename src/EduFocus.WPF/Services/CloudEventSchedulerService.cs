using System.Windows.Threading;
using EduFocus.Ports.Inbound;
using EduFocus.Ports.Outbound.Services;
using EduFocus.UseCases.Cloud;

namespace EduFocus.WPF.Services;

/// <summary>
/// Drives the cloud producers: the daily summary while protection is on and the
/// tamper scan every 30 minutes, plus a one-minute blocked-attempt watcher so
/// web blocks reach the parent quickly. Cloud outages surface as warnings only,
/// so the timers must keep running and never throw into the dispatcher.
/// </summary>
public sealed class CloudEventSchedulerService : IDisposable
{
    public static readonly TimeSpan Interval = TimeSpan.FromMinutes(30);
    public static readonly TimeSpan AlertWatchInterval = TimeSpan.FromSeconds(5);
    public static readonly TimeSpan BlockedAttemptInterval = AlertWatchInterval;

    private readonly CloudDailySummaryProducer _summaries;
    private readonly CloudSecurityAlertProducer _security;
    private readonly CloudBlockedAttemptProducer _blockedAttempts;
    private readonly IProtectionUseCase _protection;
    private readonly IAppLogger _logger;
    private readonly DispatcherTimer _timer;
    private readonly DispatcherTimer _blockedAttemptTimer;

    private bool _publishing;
    private bool _publishingBlockedAttempts;

    public CloudEventSchedulerService(
        CloudDailySummaryProducer summaries,
        CloudSecurityAlertProducer security,
        CloudBlockedAttemptProducer blockedAttempts,
        IProtectionUseCase protection,
        IAppLogger logger)
    {
        _summaries = summaries ?? throw new ArgumentNullException(nameof(summaries));
        _security = security ?? throw new ArgumentNullException(nameof(security));
        _blockedAttempts = blockedAttempts ?? throw new ArgumentNullException(nameof(blockedAttempts));
        _protection = protection ?? throw new ArgumentNullException(nameof(protection));
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));

        _timer = new DispatcherTimer(DispatcherPriority.Background)
        {
            Interval = Interval
        };
        _timer.Tick += OnTimerTick;

        _blockedAttemptTimer = new DispatcherTimer(DispatcherPriority.Background)
        {
            Interval = BlockedAttemptInterval
        };
        _blockedAttemptTimer.Tick += OnBlockedAttemptTimerTick;
    }

    /// <summary>
    /// Captures the startup snapshot, scans tamper audit rows and establishes
    /// the blocked-attempt cursor once.
    /// </summary>
    public async Task PublishStartupAsync()
    {
        await PublishAsync(requireProtectionOn: false);
        await PublishBlockedAttemptsAsync();
    }

    public void Start()
    {
        _timer.Start();
        _blockedAttemptTimer.Start();
    }

    public void Dispose()
    {
        _timer.Stop();
        _timer.Tick -= OnTimerTick;
        _blockedAttemptTimer.Stop();
        _blockedAttemptTimer.Tick -= OnBlockedAttemptTimerTick;
    }

    private async void OnTimerTick(object? sender, EventArgs e)
    {
        try
        {
            await PublishAsync(requireProtectionOn: true);
        }
        catch (Exception)
        {
        }
    }

    private async void OnBlockedAttemptTimerTick(object? sender, EventArgs e)
    {
        try
        {
            await PublishBlockedAttemptsAsync();
        }
        catch (Exception)
        {
        }
    }

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

    private async Task PublishBlockedAttemptsAsync()
    {
        if (_publishingBlockedAttempts)
            return;

        _publishingBlockedAttempts = true;

        try
        {
            await Task.Run(async () =>
            {
                await _blockedAttempts.TryPublishAsync();
                await _security.TryScanAsync();
            });
        }
        catch (Exception ex)
        {
            _logger.Warning(
                LogCategories.Cloud,
                $"The blocked-attempt watcher tick failed ({ex.GetType().Name}).");
        }
        finally
        {
            _publishingBlockedAttempts = false;
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
