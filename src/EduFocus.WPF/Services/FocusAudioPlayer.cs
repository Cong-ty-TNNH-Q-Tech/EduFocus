using System.IO;
using System.Windows.Media;
using EduFocus.Infrastructure.FocusAudio;
using EduFocus.Ports.Outbound.Services;

namespace EduFocus.WPF.Services;

/// <summary>
/// WPF implementation of the focus audio port (issue #23). Tracks are
/// synthesised on first use and cached as WAV files under LocalAppData, then
/// looped with <see cref="MediaPlayer"/> so volume and pause/resume work
/// without an external audio dependency.
/// </summary>
public sealed class FocusAudioPlayer : IFocusAudioPort
{
    private const double DefaultVolume = 0.6;

    private readonly IAppLogger _logger;
    private readonly string _cacheDirectory;

    private MediaPlayer? _player;
    private double _volume = DefaultVolume;
    private bool _playRequested;
    private bool _disposed;

    public FocusAudioPlayer(IAppLogger logger)
    {
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
        _cacheDirectory = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            "EduFocus",
            "focus-audio");
    }

    public bool IsPlaying { get; private set; }

    public FocusAudioPreset? CurrentPreset { get; private set; }

    public event EventHandler? PlaybackFailed;

    public double Volume
    {
        get => _volume;
        set
        {
            _volume = Math.Clamp(value, 0, 1);

            if (_player is not null)
                _player.Volume = _volume;
        }
    }

    public void Play(FocusAudioPreset preset)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);

        var player = EnsurePlayer();

        if (CurrentPreset != preset)
        {
            var path = EnsureWaveFile(preset);

            player.Stop();
            player.Close();
            player.Open(new Uri(path, UriKind.Absolute));
            CurrentPreset = preset;
        }

        _playRequested = true;
        player.Volume = _volume;
        player.Play();
        IsPlaying = true;
    }

    public void Stop()
    {
        _playRequested = false;
        IsPlaying = false;

        try
        {
            _player?.Pause();
        }
        catch (InvalidOperationException)
        {
            // The media session may already be closing during shutdown.
        }
    }

    public void Dispose()
    {
        if (_disposed)
            return;

        _disposed = true;
        _playRequested = false;
        IsPlaying = false;

        var player = _player;
        _player = null;

        if (player is null)
            return;

        try
        {
            if (player.Dispatcher.CheckAccess())
                player.Close();
            else if (!player.Dispatcher.HasShutdownStarted && !player.Dispatcher.HasShutdownFinished)
                _ = player.Dispatcher.BeginInvoke(player.Close);
        }
        catch (Exception ex)
        {
            _logger.Warning(
                LogCategories.UI,
                $"Could not close the focus audio player ({ex.GetType().Name}).");
        }
    }

    private MediaPlayer EnsurePlayer()
    {
        if (_player is not null)
            return _player;

        var player = new MediaPlayer();
        player.MediaOpened += (_, _) =>
        {
            player.Volume = _volume;

            if (!_playRequested)
                return;

            try
            {
                player.Play();
                IsPlaying = true;
            }
            catch (InvalidOperationException ex)
            {
                _logger.Warning(LogCategories.UI, $"Could not start focus audio ({ex.GetType().Name}).");
                _playRequested = false;
                IsPlaying = false;
                CurrentPreset = null;
                PlaybackFailed?.Invoke(this, EventArgs.Empty);
            }
        };
        player.MediaEnded += (_, _) =>
        {
            if (!_playRequested)
            {
                IsPlaying = false;
                return;
            }

            player.Position = TimeSpan.Zero;
            player.Play();
        };
        player.MediaFailed += (_, args) =>
        {
            _playRequested = false;
            IsPlaying = false;
            CurrentPreset = null;
            _logger.Warning(
                LogCategories.UI,
                $"Focus audio playback failed ({args.ErrorException?.GetType().Name ?? "unknown"}).");
            PlaybackFailed?.Invoke(this, EventArgs.Empty);
        };

        _player = player;
        return player;
    }

    private string EnsureWaveFile(FocusAudioPreset preset)
    {
        Directory.CreateDirectory(_cacheDirectory);

        var path = Path.Combine(_cacheDirectory, $"{preset.ToString().ToLowerInvariant()}.wav");

        if (File.Exists(path) && new FileInfo(path).Length > 44)
            return path;

        var wave = ProceduralAmbientAudio.CreateWave(preset);
        var tempPath = $"{path}.{Guid.NewGuid():N}.tmp";
        try
        {
            File.WriteAllBytes(tempPath, wave);
            File.Move(tempPath, path, overwrite: true);
        }
        finally
        {
            if (File.Exists(tempPath))
            {
                try { File.Delete(tempPath); } catch { }
            }
        }

        _logger.Info(
            LogCategories.UI,
            $"Generated focus audio preset '{preset}' ({wave.Length} bytes).");

        return path;
    }
}
