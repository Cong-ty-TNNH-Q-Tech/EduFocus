namespace EduFocus.Ports.Outbound.Services;

/// <summary>Looping ambient sounds for the student focus space (issue #23).</summary>
public enum FocusAudioPreset
{
    WhiteNoise,
    Rain,
    Cafe,
    Fireplace,
    Lofi
}

/// <summary>
/// Plays a looping ambient track locally. Implementations never download
/// media, so the sound keeps working offline and nothing leaves the machine.
/// </summary>
public interface IFocusAudioPort : IDisposable
{
    bool IsPlaying { get; }

    FocusAudioPreset? CurrentPreset { get; }

    /// <summary>Playback volume in the range 0..1.</summary>
    double Volume { get; set; }

    void Play(FocusAudioPreset preset);

    void Stop();
}
