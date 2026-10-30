using System.Windows.Threading;
using EduFocus.Ports.DTOs;
using EduFocus.Ports.Inbound;
using EduFocus.Ports.Outbound.Services;
using EduFocus.Ports.ServiceControl;
using EduFocus.UseCases.Protection;
using EduFocus.WPF.ViewModels;

namespace EduFocus.WPF.Services;

/// <summary>
/// Polls the protection service so transitions the service makes on its own
/// (schedule windows, recovery after reboot) reach the tray and the monitor
/// overlay without a UI action. The poll is also the only status path when the
/// service is reachable, so a failure stops camera/activity monitoring until
/// the next successful poll.
/// </summary>
public sealed class ServiceStatusPoller : IDisposable
{
    private static readonly TimeSpan Interval = TimeSpan.FromSeconds(10);

    private readonly IProtectionUseCase _protection;
    private readonly ProtectionStatusViewModel _viewModel;
    private readonly ServiceMonitoringCoordinator _monitoring;
    private readonly IAppLogger _logger;
    private readonly DispatcherTimer _timer;

    private bool _refreshing;

    public ServiceStatusPoller(
        IProtectionUseCase protection,
        ProtectionStatusViewModel viewModel,
        ServiceMonitoringCoordinator monitoring,
        IAppLogger logger)
    {
        _protection = protection ?? throw new ArgumentNullException(nameof(protection));
        _viewModel = viewModel ?? throw new ArgumentNullException(nameof(viewModel));
        _monitoring = monitoring ?? throw new ArgumentNullException(nameof(monitoring));
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));

        _timer = new DispatcherTimer(DispatcherPriority.Background)
        {
            Interval = Interval
        };
        _timer.Tick += OnTimerTick;
    }

    public void Start() => _timer.Start();

    public async Task RefreshOnceAsync(CancellationToken cancellationToken = default)
    {
        if (_viewModel.IsBusy)
            return;

        ProtectionStatus status;

        try
        {
            status = await Task.Run(
                () => _protection.GetStatusAsync(cancellationToken),
                cancellationToken);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            return;
        }
        catch (ServiceControlException ex)
        {
            _logger.Error(LogCategories.Protection, "The protection service status poll failed.", ex);
            _viewModel.ReportFailure(
                "Không thể kết nối tới dịch vụ bảo vệ EduFocus. Camera và hoạt động tạm dừng cho tới khi dịch vụ phản hồi.");

            // The service is the single owner of protection state; without a
            // trustworthy status the interactive sensors must stay off.
            await _monitoring.StopAsync(CancellationToken.None);
            return;
        }
        catch (Exception ex)
        {
            _logger.Error(LogCategories.Protection, "The protection service status poll failed.", ex);
            _viewModel.ReportFailure(
                "Trạng thái bảo vệ chưa được đồng bộ với dịch vụ. Vui lòng thử lại.");

            await _monitoring.StopAsync(CancellationToken.None);
            return;
        }

        try
        {
            _viewModel.Apply(status, []);
            await _monitoring.SyncAsync(status, cancellationToken);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
        }
        catch (Exception ex)
        {
            _logger.Error(LogCategories.Protection, "Could not apply the polled protection status.", ex);
        }
    }

    private async void OnTimerTick(object? sender, EventArgs e)
    {
        if (_refreshing)
            return;

        _refreshing = true;

        try
        {
            await RefreshOnceAsync();
        }
        catch (Exception ex)
        {
            _logger.Error(LogCategories.Protection, "The protection service status poll failed.", ex);
        }
        finally
        {
            _refreshing = false;
        }
    }

    public void Dispose()
    {
        _timer.Stop();
        _timer.Tick -= OnTimerTick;
    }
}
