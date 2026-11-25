using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using EduFocus.Ports.Outbound.Services;

namespace EduFocus.WPF.ViewModels;

public sealed partial class FocusAudioPresetOption : ObservableObject
{
    public FocusAudioPresetOption(FocusAudioPreset preset, string title, string description)
    {
        Preset = preset;
        Title = title;
        Description = description;
    }

    public FocusAudioPreset Preset { get; }

    public string Title { get; }

    public string Description { get; }

    [ObservableProperty] private bool _isSelected;
}

/// <summary>
/// Mini ambient-sound player for the student focus space (issue #23). Tracks
/// are generated locally and loop until the student pauses them.
/// </summary>
public sealed partial class FocusAudioViewModel : ObservableObject
{
    private readonly IFocusAudioPort _audio;
    private readonly IAppLogger _logger;

    [ObservableProperty] private FocusAudioPresetOption _selectedPreset;
    [ObservableProperty] private bool _isPlaying;
    [ObservableProperty] private double _volume = 60;
    [ObservableProperty] private string _statusText = "Chọn một âm thanh và bấm Phát khi bắt đầu học.";
    [ObservableProperty] private bool _hasError;
    [ObservableProperty] private string _errorMessage = string.Empty;

    public FocusAudioViewModel(IFocusAudioPort audio, IAppLogger logger)
    {
        _audio = audio ?? throw new ArgumentNullException(nameof(audio));
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));

        Presets =
        [
            new(
                FocusAudioPreset.WhiteNoise,
                "Tiếng ồn trắng",
                "Làm mờ tiếng ồn xung quanh để dễ tập trung."),
            new(
                FocusAudioPreset.Rain,
                "Mưa rơi",
                "Tiếng mưa đều nhẹ giúp thư giãn."),
            new(
                FocusAudioPreset.Cafe,
                "Quán cà phê",
                "Không khí quán ấm áp, ít xao nhãng."),
            new(
                FocusAudioPreset.Fireplace,
                "Lò sưởi",
                "Tiếng lửa tách tách ấm áp."),
            new(
                FocusAudioPreset.Lofi,
                "Lo-fi nhẹ",
                "Giai điệu êm và tiếng đĩa than nhẹ.")
        ];

        _selectedPreset = Presets[0];
        _selectedPreset.IsSelected = true;
        _audio.Volume = Volume / 100.0;
        _audio.PlaybackFailed += OnPlaybackFailed;
    }

    public IReadOnlyList<FocusAudioPresetOption> Presets { get; }

    public string PlayButtonText => IsPlaying ? "Tạm dừng" : "Phát";

    partial void OnIsPlayingChanged(bool value) => OnPropertyChanged(nameof(PlayButtonText));

    partial void OnVolumeChanged(double value) =>
        _audio.Volume = Math.Clamp(value / 100.0, 0, 1);

    [RelayCommand]
    private void SelectPreset(FocusAudioPresetOption? option)
    {
        if (option is null)
            return;

        if (!ReferenceEquals(option, SelectedPreset))
        {
            SelectedPreset.IsSelected = false;
            SelectedPreset = option;
            option.IsSelected = true;
        }

        Start();
    }

    [RelayCommand]
    private void Toggle()
    {
        if (IsPlaying)
            Stop();
        else
            Start();
    }

    private void Start()
    {
        try
        {
            _audio.Play(SelectedPreset.Preset);
            IsPlaying = true;
            HasError = false;
            ErrorMessage = string.Empty;
            StatusText = $"Đang phát: {SelectedPreset.Title}";
        }
        catch (Exception ex)
        {
            _logger.Error(LogCategories.UI, "Could not play the focus audio.", ex);
            IsPlaying = false;
            HasError = true;
            ErrorMessage = "Không thể phát âm thanh trên máy này. Hãy thử lại hoặc chọn âm thanh khác.";
        }
    }

    private void Stop()
    {
        try
        {
            _audio.Stop();
        }
        catch (Exception ex)
        {
            _logger.Warning(
                LogCategories.UI,
                $"Could not pause the focus audio ({ex.GetType().Name}).");
        }

        IsPlaying = false;
        StatusText = "Đã tạm dừng. Bấm Phát để tiếp tục.";
    }

    private void OnPlaybackFailed(object? sender, EventArgs e)
    {
        IsPlaying = false;
        HasError = true;
        ErrorMessage = "Không thể phát âm thanh trên máy này. Hãy thử lại hoặc chọn âm thanh khác.";
        StatusText = "Phát âm thanh không thành công.";
    }
}
