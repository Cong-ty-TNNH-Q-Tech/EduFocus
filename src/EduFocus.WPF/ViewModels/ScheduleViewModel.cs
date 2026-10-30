using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using EduFocus.Domain.Entities;
using EduFocus.Domain.Enums;
using EduFocus.Ports.DTOs;
using EduFocus.Ports.Inbound;
using EduFocus.Ports.Outbound.Services;
using EduFocus.Ports.Security;

namespace EduFocus.WPF.ViewModels;

/// <summary>
/// Parent Mode schedule editor (UC06). The use case owns validation, persistence
/// and automatic transitions; this view model only projects the overview, sends
/// parent drafts and refreshes the shared protection status after transitions.
/// </summary>
public sealed partial class ScheduleViewModel : ObservableObject
{
    private readonly IScheduleUseCase _schedule;
    private readonly ProtectionStatusViewModel _protection;
    private readonly IAppLogger _logger;
    private readonly TimeProvider _timeProvider;

    private ScheduleOverview? _overview;
    private Guid? _editingId;

    [ObservableProperty] private ScheduleWindowRow? _selectedWindow;
    [ObservableProperty] private bool _scheduleEnabled;
    [ObservableProperty] private string _scheduleStatusText = "Đang đọc lịch bảo vệ...";
    [ObservableProperty] private string _statusMessage = string.Empty;
    [ObservableProperty] private bool _statusIsError;
    [ObservableProperty] private bool _isBusy;

    [ObservableProperty] private bool _monday;
    [ObservableProperty] private bool _tuesday;
    [ObservableProperty] private bool _wednesday;
    [ObservableProperty] private bool _thursday;
    [ObservableProperty] private bool _friday;
    [ObservableProperty] private bool _saturday;
    [ObservableProperty] private bool _sunday;
    [ObservableProperty] private string _startTime = "08:00";
    [ObservableProperty] private string _endTime = "11:30";
    [ObservableProperty] private string _windowLabel = string.Empty;
    [ObservableProperty] private bool _windowEnabled = true;

    public ScheduleViewModel(
        IScheduleUseCase schedule,
        ProtectionStatusViewModel protection,
        IAppLogger logger,
        TimeProvider timeProvider)
    {
        _schedule = schedule ?? throw new ArgumentNullException(nameof(schedule));
        _protection = protection ?? throw new ArgumentNullException(nameof(protection));
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
        _timeProvider = timeProvider ?? throw new ArgumentNullException(nameof(timeProvider));
    }

    public ObservableCollection<ScheduleWindowRow> Windows { get; } = [];

    public bool HasWindows => Windows.Count > 0;

    public bool HasNoWindows => Windows.Count == 0;

    public bool CanDelete => _editingId is not null;

    public string EditorTitle => _editingId is null ? "Thêm khung giờ mới" : "Sửa khung giờ";

    public string SaveButtonText => _editingId is null ? "Thêm khung giờ" : "Lưu thay đổi";

    public string ScheduleToggleText => ScheduleEnabled ? "Tắt lịch bảo vệ" : "Bật lịch bảo vệ";

    public bool IsNotBusy => !IsBusy;

    public bool HasStatusMessage => !string.IsNullOrEmpty(StatusMessage);

    partial void OnScheduleEnabledChanged(bool value) =>
        OnPropertyChanged(nameof(ScheduleToggleText));

    partial void OnIsBusyChanged(bool value) => OnPropertyChanged(nameof(IsNotBusy));

    partial void OnStatusMessageChanged(string value) =>
        OnPropertyChanged(nameof(HasStatusMessage));

    partial void OnSelectedWindowChanged(ScheduleWindowRow? value)
    {
        if (value is null)
            return;

        _editingId = value.Id;
        Monday = (value.Days & ScheduleDays.Monday) != 0;
        Tuesday = (value.Days & ScheduleDays.Tuesday) != 0;
        Wednesday = (value.Days & ScheduleDays.Wednesday) != 0;
        Thursday = (value.Days & ScheduleDays.Thursday) != 0;
        Friday = (value.Days & ScheduleDays.Friday) != 0;
        Saturday = (value.Days & ScheduleDays.Saturday) != 0;
        Sunday = (value.Days & ScheduleDays.Sunday) != 0;
        StartTime = ScheduleText.FormatMinutes(value.StartMinutes);
        EndTime = ScheduleText.FormatMinutes(value.EndMinutes);
        WindowLabel = value.Label ?? string.Empty;
        WindowEnabled = value.IsEnabled;
        NotifyEditorChanged();
    }

    /// <summary>Loads the stored schedule when Parent Mode opens. Never throws.</summary>
    public async Task RefreshAsync(CancellationToken cancellationToken = default)
    {
        try
        {
            ApplyOverview(await _schedule.GetOverviewAsync(cancellationToken));
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
        }
        catch (Exception ex)
        {
            _logger.Error(LogCategories.Protection, "Could not read the protection schedule.", ex);
            SetStatus("Không thể đọc lịch bảo vệ. Vui lòng thử lại.", true);
        }
    }

    /// <summary>Surfaces a scheduler failure in the Parent Mode schedule card.</summary>
    public void ReportFailure(string message) => SetStatus(message, true);

    /// <summary>Applies a 30-second evaluation without resetting the editor.</summary>
    public async Task ApplyEvaluationAsync(
        ScheduleEvaluationResult result,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(result);

        ApplyOverview(result.Overview);

        if (result.ProtectionChanged)
            await _protection.RefreshAsync(cancellationToken);

        SetWarnings(result.Warnings);
    }

    [RelayCommand]
    private async Task ToggleScheduleAsync()
    {
        if (IsBusy)
            return;

        var target = !ScheduleEnabled;
        IsBusy = true;

        try
        {
            var result = await Task.Run(() => _schedule.SetScheduleEnabledAsync(target));
            await ApplyOperationAsync(result);

            SetSuccessUnlessWarnings(
                result.Warnings,
                target ? "Đã bật lịch bảo vệ." : "Đã tắt lịch bảo vệ.");
        }
        catch (ParentAuthorizationException)
        {
            SetStatus("Phiên Parent Mode đã hết hạn. Vui lòng đăng nhập lại.", true);
        }
        catch (Exception ex)
        {
            _logger.Error(LogCategories.Protection, "Could not toggle the protection schedule.", ex);
            SetStatus("Không thể đổi trạng thái lịch bảo vệ. Vui lòng thử lại.", true);
        }
        finally
        {
            IsBusy = false;
        }
    }

    [RelayCommand]
    private async Task SaveWindowAsync()
    {
        if (IsBusy)
            return;

        if (!TryBuildDraft(out var draft, out var validationMessage))
        {
            SetStatus(validationMessage, true);
            return;
        }

        var editingId = _editingId;
        IsBusy = true;

        try
        {
            var result = editingId is { } id
                ? await Task.Run(() => _schedule.UpdateWindowAsync(id, draft))
                : await Task.Run(() => _schedule.AddWindowAsync(draft));

            await ApplyOperationAsync(result);

            if (editingId is null)
            {
                ResetEditor();
                SetSuccessUnlessWarnings(result.Warnings, "Đã thêm khung giờ.");
            }
            else
            {
                SelectedWindow = Windows.FirstOrDefault(row => row.Id == editingId);
                if (SelectedWindow is null)
                    ResetEditor();

                SetSuccessUnlessWarnings(result.Warnings, "Đã lưu khung giờ.");
            }
        }
        catch (ParentAuthorizationException)
        {
            SetStatus("Phiên Parent Mode đã hết hạn. Vui lòng đăng nhập lại.", true);
        }
        catch (ArgumentException)
        {
            SetStatus("Khung giờ trùng với một khung giờ đang bật khác hoặc không hợp lệ.", true);
        }
        catch (Exception ex)
        {
            _logger.Error(LogCategories.Protection, "Could not save the protection schedule.", ex);
            SetStatus("Không thể lưu khung giờ. Vui lòng thử lại.", true);
        }
        finally
        {
            IsBusy = false;
        }
    }

    [RelayCommand]
    private async Task DeleteWindowAsync()
    {
        if (IsBusy || _editingId is not { } id)
            return;

        IsBusy = true;

        try
        {
            var result = await Task.Run(() => _schedule.DeleteWindowAsync(id));
            await ApplyOperationAsync(result);
            ResetEditor();
            SetSuccessUnlessWarnings(result.Warnings, "Đã xóa khung giờ.");
        }
        catch (ParentAuthorizationException)
        {
            SetStatus("Phiên Parent Mode đã hết hạn. Vui lòng đăng nhập lại.", true);
        }
        catch (Exception ex)
        {
            _logger.Error(LogCategories.Protection, "Could not delete a schedule window.", ex);
            SetStatus("Không thể xóa khung giờ. Vui lòng thử lại.", true);
        }
        finally
        {
            IsBusy = false;
        }
    }

    [RelayCommand]
    private void NewWindow()
    {
        ResetEditor();
        SetStatus("Chọn ngày và khung giờ rồi bấm Thêm khung giờ.", false);
    }

    private async Task ApplyOperationAsync(ScheduleOperationResult result)
    {
        ApplyOverview(result.Overview);

        if (result.ProtectionChanged)
            await _protection.RefreshAsync();

        SetWarnings(result.Warnings);
    }

    private void ApplyOverview(ScheduleOverview overview)
    {
        _overview = overview;
        ScheduleEnabled = overview.ScheduleEnabled;
        RebuildWindows(overview);
        UpdateScheduleStatusText();
    }

    private void RebuildWindows(ScheduleOverview overview)
    {
        var incoming = overview.Windows.Select(info => new ScheduleWindowRow(info)).ToList();

        if (!Windows.Select(row => row.Key).SequenceEqual(incoming.Select(row => row.Key)))
        {
            var selectedId = SelectedWindow?.Id ?? _editingId;

            Windows.Clear();
            foreach (var row in incoming)
                Windows.Add(row);

            OnPropertyChanged(nameof(HasWindows));
            OnPropertyChanged(nameof(HasNoWindows));

            SelectedWindow = selectedId is { } id
                ? Windows.FirstOrDefault(row => row.Id == id)
                : null;

            if (selectedId is not null && SelectedWindow is null && _editingId == selectedId)
                ResetEditor();
        }
    }

    private void UpdateScheduleStatusText()
    {
        if (_overview is not { } overview)
        {
            ScheduleStatusText = "Đang đọc lịch bảo vệ...";
            return;
        }

        if (!overview.ScheduleEnabled)
        {
            ScheduleStatusText =
                "Lịch bảo vệ đang tắt. Bảo vệ chỉ thay đổi khi bạn bật hoặc tắt thủ công.";
            return;
        }

        if (overview.Windows.Count == 0)
        {
            ScheduleStatusText = "Lịch đang bật nhưng chưa có khung giờ nào.";
            return;
        }

        if (overview.ActiveWindow is { } active)
        {
            var range =
                $"{ScheduleText.FormatMinutes(active.StartMinutes)}–{ScheduleText.FormatMinutes(active.EndMinutes)}";

            if (overview.IsOverridden && overview.OverrideUntilUtc is { } until)
            {
                var localUntil = TimeZoneInfo.ConvertTimeFromUtc(until, _timeProvider.LocalTimeZone);
                ScheduleStatusText =
                    $"Đang trong khung giờ {range} nhưng bạn đã tạm tắt bảo vệ đến {localUntil:HH:mm}.";
                return;
            }

            ScheduleStatusText = overview.ProtectionEnabled
                ? $"Đang trong khung giờ {range}. Bảo vệ đang bật."
                : $"Đang trong khung giờ {range}. Bảo vệ sẽ tự bật ở lần đánh giá kế tiếp.";
            return;
        }

        ScheduleStatusText = overview.ProtectionEnabled
            ? overview.ProtectionSource == ProtectionSource.Manual
                ? "Ngoài khung giờ. Bảo vệ đang bật thủ công."
                : "Ngoài khung giờ. Bảo vệ đang bật."
            : "Ngoài khung giờ. Bảo vệ sẽ tự bật khi đến khung giờ tiếp theo.";
    }

    private bool TryBuildDraft(out ScheduleWindowDraft draft, out string message)
    {
        draft = null!;

        var days = SelectedDays();
        if (days == ScheduleDays.None)
        {
            message = "Chọn ít nhất một ngày cho khung giờ.";
            return false;
        }

        if (!ScheduleText.TryParseTime(StartTime, allowEndOfDay: false, out var startMinutes))
        {
            message = "Giờ bắt đầu không hợp lệ. Dùng định dạng HH:mm.";
            return false;
        }

        if (!ScheduleText.TryParseTime(EndTime, allowEndOfDay: true, out var endMinutes))
        {
            message = "Giờ kết thúc không hợp lệ. Dùng định dạng HH:mm hoặc 24:00.";
            return false;
        }

        if (endMinutes <= startMinutes)
        {
            message = "Giờ kết thúc phải sau giờ bắt đầu. Khung giờ không vắt qua nửa đêm.";
            return false;
        }

        var label = string.IsNullOrWhiteSpace(WindowLabel) ? null : WindowLabel.Trim();
        if (label is { Length: > ScheduleWindow.MaxLabelLength })
        {
            message = $"Nhãn tối đa {ScheduleWindow.MaxLabelLength} ký tự.";
            return false;
        }

        draft = new ScheduleWindowDraft(days, startMinutes, endMinutes, label, WindowEnabled);
        message = string.Empty;
        return true;
    }

    private ScheduleDays SelectedDays()
    {
        var days = ScheduleDays.None;

        if (Monday)
            days |= ScheduleDays.Monday;
        if (Tuesday)
            days |= ScheduleDays.Tuesday;
        if (Wednesday)
            days |= ScheduleDays.Wednesday;
        if (Thursday)
            days |= ScheduleDays.Thursday;
        if (Friday)
            days |= ScheduleDays.Friday;
        if (Saturday)
            days |= ScheduleDays.Saturday;
        if (Sunday)
            days |= ScheduleDays.Sunday;

        return days;
    }

    private void ResetEditor()
    {
        _editingId = null;
        SelectedWindow = null;
        Monday = false;
        Tuesday = false;
        Wednesday = false;
        Thursday = false;
        Friday = false;
        Saturday = false;
        Sunday = false;
        StartTime = "08:00";
        EndTime = "11:30";
        WindowLabel = string.Empty;
        WindowEnabled = true;
        NotifyEditorChanged();
    }

    private void NotifyEditorChanged()
    {
        OnPropertyChanged(nameof(CanDelete));
        OnPropertyChanged(nameof(EditorTitle));
        OnPropertyChanged(nameof(SaveButtonText));
    }

    private void SetSuccessUnlessWarnings(IReadOnlyList<string> warnings, string message)
    {
        if (warnings.Count == 0)
            SetStatus(message, false);
    }

    private void SetWarnings(IReadOnlyList<string> warnings)
    {
        if (warnings.Count > 0)
            SetStatus(string.Join(Environment.NewLine, warnings), true);
    }

    private void SetStatus(string message, bool isError)
    {
        StatusMessage = message;
        StatusIsError = isError;
    }
}
