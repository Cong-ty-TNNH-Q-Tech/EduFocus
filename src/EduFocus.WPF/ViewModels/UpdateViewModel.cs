using System;
using System.Threading.Tasks;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using EduFocus.Ports.Inbound;
using EduFocus.Ports.Outbound.Persistence;
using EduFocus.Ports.Outbound.Services;
using EduFocus.WPF.Services;

namespace EduFocus.WPF.ViewModels;

public partial class UpdateViewModel : ObservableObject
{
    private readonly UpdaterService _updater;
    private readonly IAppSettingsRepository _settings;
    private readonly IParentAuthorizer? _authorizer;
    private readonly IProtectionUseCase? _protection;
    private readonly AppExitState? _exitState;

    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(CheckForUpdateCommand))]
    [NotifyCanExecuteChangedFor(nameof(DownloadAndInstallCommand))]
    private bool _isChecking;

    [ObservableProperty] private bool _isUpdateAvailable;

    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(CheckForUpdateCommand))]
    [NotifyCanExecuteChangedFor(nameof(DownloadAndInstallCommand))]
    private bool _isDownloading;

    [ObservableProperty] private int  _downloadProgress;
    [ObservableProperty] private string _statusMessage = string.Empty;
    [ObservableProperty] private string _latestVersion = string.Empty;
    [ObservableProperty] private string _releaseNotes  = string.Empty;

    private ReleaseInfo? _pendingRelease;

    public string CurrentVersion => UpdaterService.CurrentVersion;

    public bool CanInteract => !IsChecking && !IsDownloading;

    public UpdateViewModel(
        UpdaterService updater,
        IAppSettingsRepository settings,
        IParentAuthorizer? authorizer = null,
        IProtectionUseCase? protection = null,
        AppExitState? exitState = null)
    {
        _updater = updater;
        _settings = settings;
        _authorizer = authorizer;
        _protection = protection;
        _exitState = exitState;
    }

    [RelayCommand(CanExecute = nameof(CanInteract))]
    public async Task CheckForUpdateAsync()
    {
        if (IsChecking || IsDownloading)
            return;

        IsChecking      = true;
        IsUpdateAvailable = false;
        StatusMessage   = "Đang kiểm tra cập nhật...";
        try
        {
            var settings = await _settings.GetAsync();
            if (settings?.UpdateCheckEnabled != true)
            {
                StatusMessage = "Kiểm tra cập nhật đang tắt. Bật trong Dữ liệu & Quyền riêng tư.";
                return;
            }

            _pendingRelease = await _updater.CheckForUpdateAsync();
            if (_pendingRelease != null)
            {
                LatestVersion     = _pendingRelease.Version;
                ReleaseNotes      = _pendingRelease.ReleaseNotes;
                IsUpdateAvailable = true;
                StatusMessage     = $"Phiên bản mới: v{_pendingRelease.Version}";
            }
            else
            {
                StatusMessage = $"Đang dùng phiên bản mới nhất (v{CurrentVersion})";
            }
        }
        catch (Exception ex)
        {
            StatusMessage = $"Lỗi kiểm tra: {ex.Message}";
        }
        finally
        {
            IsChecking = false;
        }
    }

    [RelayCommand(CanExecute = nameof(CanInteract))]
    public async Task DownloadAndInstallAsync()
    {
        if (_pendingRelease == null || IsChecking || IsDownloading)
            return;

        var settings = await _settings.GetAsync();
        if (settings?.ProtectionEnabled == true && _authorizer is { IsAuthenticated: false })
        {
            StatusMessage = "Đang trong phiên bảo vệ. Vui lòng mở Parent Mode (nhập PIN phụ huynh) trước khi cài đặt cập nhật.";
            return;
        }

        IsDownloading   = true;
        StatusMessage   = "Đang tải về...";
        DownloadProgress = 0;

        var progress = new Progress<int>(p =>
        {
            DownloadProgress = p;
            StatusMessage    = $"Đang tải... {p}%";
        });

        try
        {
            await _updater.DownloadAndApplyAsync(
                _pendingRelease,
                progress,
                beforeRestartAsync: async () =>
                {
                    StatusMessage = "Đang cài đặt và khởi động lại...";
                    if (settings?.ProtectionEnabled == true && _protection is not null)
                    {
                        try
                        {
                            await _protection.DisableForExitAsync();
                        }
                        catch
                        {
                        }
                    }

                    _exitState?.ApproveExit();
                });
        }
        catch (Exception ex)
        {
            StatusMessage = $"Lỗi cài đặt: {ex.Message}";
            IsDownloading = false;
        }
    }
}
