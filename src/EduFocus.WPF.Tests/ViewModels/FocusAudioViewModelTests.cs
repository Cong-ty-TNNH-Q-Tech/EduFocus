using EduFocus.Ports.Outbound.Services;
using EduFocus.WPF.Tests.TestSupport;
using EduFocus.WPF.ViewModels;
using FluentAssertions;

namespace EduFocus.WPF.Tests.ViewModels;

public sealed class FocusAudioViewModelTests
{
    private readonly FakeFocusAudioPort _audio = new();
    private readonly RecordingLogger _logger = new();

    [Fact]
    public void Defaults_select_the_first_preset_and_apply_the_volume()
    {
        var sut = CreateSut();

        sut.SelectedPreset.Preset.Should().Be(FocusAudioPreset.WhiteNoise);
        sut.SelectedPreset.IsSelected.Should().BeTrue();
        sut.IsPlaying.Should().BeFalse();
        sut.PlayButtonText.Should().Be("Phát");
        _audio.Volume.Should().BeApproximately(0.6, 0.0001);
    }

    [Fact]
    public void Selecting_a_preset_plays_it_and_marks_only_that_chip()
    {
        var sut = CreateSut();
        var rain = sut.Presets.Single(option => option.Preset == FocusAudioPreset.Rain);

        sut.SelectPresetCommand.Execute(rain);

        sut.SelectedPreset.Should().BeSameAs(rain);
        rain.IsSelected.Should().BeTrue();
        sut.Presets.Where(option => option != rain).Should().OnlyContain(option => !option.IsSelected);
        _audio.PlayCalls.Should().Be(1);
        _audio.CurrentPreset.Should().Be(FocusAudioPreset.Rain);
        sut.IsPlaying.Should().BeTrue();
        sut.PlayButtonText.Should().Be("Tạm dừng");
    }

    [Fact]
    public void Toggle_pauses_and_resumes_the_selected_preset()
    {
        var sut = CreateSut();

        sut.ToggleCommand.Execute(null);
        sut.IsPlaying.Should().BeTrue();
        _audio.PlayCalls.Should().Be(1);

        sut.ToggleCommand.Execute(null);
        sut.IsPlaying.Should().BeFalse();
        _audio.StopCalls.Should().Be(1);
        sut.PlayButtonText.Should().Be("Phát");
    }

    [Fact]
    public void Volume_changes_are_forwarded_to_the_port_as_a_fraction()
    {
        var sut = CreateSut();

        sut.Volume = 35;

        _audio.Volume.Should().BeApproximately(0.35, 0.0001);
    }

    [Fact]
    public void Playback_failure_is_reported_without_crashing()
    {
        var sut = CreateSut();
        _audio.PlayException = new InvalidOperationException("no audio device");

        sut.ToggleCommand.Execute(null);

        sut.IsPlaying.Should().BeFalse();
        sut.HasError.Should().BeTrue();
        sut.ErrorMessage.Should().NotBeNullOrWhiteSpace();
        _logger.Errors.Should().ContainSingle();
    }

    [Fact]
    public void Async_playback_failure_resets_playing_state_and_reports_error()
    {
        var sut = CreateSut();
        sut.ToggleCommand.Execute(null);
        sut.IsPlaying.Should().BeTrue();

        _audio.SimulateAsyncFailure();

        sut.IsPlaying.Should().BeFalse();
        sut.PlayButtonText.Should().Be("Phát");
        sut.HasError.Should().BeTrue();
        sut.ErrorMessage.Should().NotBeNullOrWhiteSpace();
    }

    private FocusAudioViewModel CreateSut() => new(_audio, _logger);

    private sealed class FakeFocusAudioPort : IFocusAudioPort
    {
        public bool IsPlaying { get; private set; }

        public FocusAudioPreset? CurrentPreset { get; private set; }

        public double Volume { get; set; } = 1;

        public int PlayCalls { get; private set; }

        public int StopCalls { get; private set; }

        public Exception? PlayException { get; set; }

        public event EventHandler? PlaybackFailed;

        public void SimulateAsyncFailure()
        {
            IsPlaying = false;
            CurrentPreset = null;
            PlaybackFailed?.Invoke(this, EventArgs.Empty);
        }

        public void Play(FocusAudioPreset preset)
        {
            PlayCalls++;

            if (PlayException is not null)
                throw PlayException;

            CurrentPreset = preset;
            IsPlaying = true;
        }

        public void Stop()
        {
            StopCalls++;
            IsPlaying = false;
        }

        public void Dispose()
        {
        }
    }
}
