using System.Windows.Threading;
using EduFocus.Ports.Inbound;
using EduFocus.Ports.Outbound.Services;

namespace EduFocus.WPF.Services;

/// <summary>
/// Runs retention once after startup and then once per local day at 03:00
/// (UC11). The pass runs on the thread pool so batched deletes never hold the
/// UI thread; failures are logged and retried on the next startup or day.
/// </summary>
public sealed class RetentionSchedulerService : IDisposable
{
    private static readonly TimeSpan CheckInterval = TimeSpan.FromMinutes(1);
    private const int DailyHour = 3;

    private readonly IRetentionUseCase _retention;
    private readonly IAppLogger _logger;
    private readonly DispatcherTimer _timer;

    private DateOnly? _lastRunLocalDate;
    private bool _running;

    public RetentionSchedulerService(IRetentionUseCase retention, IAppLogger logger)
    {
        _retention = retention ?? throw new ArgumentNullException(nameof(retention));
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));

        _timer = new DispatcherTimer(DispatcherPriority.Background)
        {
            Interval = CheckInterval
        };
        _timer.Tick += OnTimerTick;
    }

    public void Start()
    {
        _timer.Start();
        _ = RunAsync(force: true);
    }

    public void Dispose()
    {
        _timer.Stop();
        _timer.Tick -= OnTimerTick;
    }

    private async void OnTimerTick(object? sender, EventArgs e)
    {
        var now = DateTime.Now;
        var today = DateOnly.FromDateTime(now);

        if (now.Hour < DailyHour || _lastRunLocalDate == today)
            return;

        await RunAsync(force: false);
    }

    private async Task RunAsync(bool force)
    {
        if (_running)
            return;

        var today = DateOnly.FromDateTime(DateTime.Now);
        if (!force && _lastRunLocalDate == today)
            return;

        _running = true;

        try
        {
            var result = await Task.Run(() => _retention.RunOnceAsync()).ConfigureAwait(true);

            if (result.Completed)
            {
                _lastRunLocalDate = today;
                _logger.Info(LogCategories.Database, "Daily retention completed.");
            }
            else
            {
                _logger.Warning(
                    LogCategories.Database,
                    $"Daily retention finished with issues: {string.Join(", ", result.Failures)}.");
            }
        }
        catch (Exception ex)
        {
            _logger.Error(LogCategories.Database, "Daily retention failed.", ex);
        }
        finally
        {
            _running = false;
        }
    }
}
