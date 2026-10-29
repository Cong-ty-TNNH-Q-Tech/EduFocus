using System.Windows.Threading;
using EduFocus.Ports.Inbound;
using EduFocus.Ports.Outbound.Services;
using EduFocus.WPF.ViewModels;

namespace EduFocus.WPF.Services;

/// <summary>
/// Drives the UC06 schedule evaluator every 30 seconds and once at startup.
/// Engine failures are reported as warnings by the use case, so this loop must
/// stay alive and never throw into the dispatcher.
/// </summary>
public sealed class ScheduleSchedulerService : IDisposable
{
    private static readonly TimeSpan Interval = TimeSpan.FromSeconds(30);

    private readonly IScheduleUseCase _schedule;
    private readonly ScheduleViewModel _viewModel;
    private readonly IAppLogger _logger;
    private readonly DispatcherTimer _timer;

    private bool _evaluating;

    public ScheduleSchedulerService(
        IScheduleUseCase schedule,
        ScheduleViewModel viewModel,
        IAppLogger logger)
    {
        _schedule = schedule ?? throw new ArgumentNullException(nameof(schedule));
        _viewModel = viewModel ?? throw new ArgumentNullException(nameof(viewModel));
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));

        _timer = new DispatcherTimer(DispatcherPriority.Background)
        {
            Interval = Interval
        };
        _timer.Tick += OnTimerTick;
    }

    /// <summary>Runs one evaluation on the thread pool and projects it to the UI.</summary>
    public async Task EvaluateNowAsync()
    {
        if (_evaluating)
            return;

        _evaluating = true;

        try
        {
            var result = await Task.Run(() => _schedule.EvaluateAsync());
            await _viewModel.ApplyEvaluationAsync(result);
        }
        catch (Exception ex)
        {
            _logger.Error(LogCategories.Protection, "Schedule evaluation failed.", ex);
            _viewModel.ReportFailure(
                "Không thể đánh giá lịch bảo vệ. Hệ thống sẽ thử lại ở lần cập nhật kế tiếp.");
        }
        finally
        {
            _evaluating = false;
        }
    }

    public void Start() => _timer.Start();

    public void Dispose()
    {
        _timer.Stop();
        _timer.Tick -= OnTimerTick;
    }

    private async void OnTimerTick(object? sender, EventArgs e) => await EvaluateNowAsync();
}
