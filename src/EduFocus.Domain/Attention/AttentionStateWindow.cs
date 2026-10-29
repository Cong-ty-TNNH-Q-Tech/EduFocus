namespace EduFocus.Domain.Attention;

/// <summary>
/// Sliding-window smoother over recent classifications (5–10 seconds at the
/// sampling cadence). A raw state is only produced when a majority of samples
/// in the window agree, so a single noisy frame cannot start a state change.
/// Camera-unavailable samples bypass the window because technical failures must
/// be recorded immediately.
/// </summary>
public sealed class AttentionStateWindow
{
    private readonly TimeSpan _windowDuration;
    private readonly int _minimumSamples;
    private readonly double _majorityShare;
    private readonly List<Entry> _entries = [];

    public AttentionStateWindow(
        TimeSpan windowDuration,
        int minimumSamples = 3,
        double majorityShare = 0.6)
    {
        if (windowDuration <= TimeSpan.Zero)
            throw new ArgumentOutOfRangeException(nameof(windowDuration), "Window duration must be positive.");
        if (windowDuration > TimeSpan.FromMinutes(1))
            throw new ArgumentOutOfRangeException(nameof(windowDuration), "Window duration must not exceed one minute.");
        if (minimumSamples < 2)
            throw new ArgumentOutOfRangeException(nameof(minimumSamples), "At least two samples are required.");
        if (majorityShare is <= 0 or > 1)
            throw new ArgumentOutOfRangeException(nameof(majorityShare), "Majority share must be between 0 and 1.");

        _windowDuration = windowDuration;
        _minimumSamples = minimumSamples;
        _majorityShare = majorityShare;
    }

    public int SampleCount => _entries.Count;

    public AttentionClassification? Observe(AttentionClassification classification, DateTimeOffset now)
    {
        ArgumentNullException.ThrowIfNull(classification);

        if (classification.State == RawAttentionState.CameraUnavailable)
        {
            Reset();
            return classification;
        }

        Prune(now);
        _entries.Add(new Entry(now, classification));

        if (_entries.Count < _minimumSamples)
            return null;

        var best = _entries
            .GroupBy(entry => entry.Classification.State)
            .Select(group => new
            {
                State = group.Key,
                Count = group.Count(),
                Latest = group.Max(entry => entry.At),
                Confidence = (float)group.Average(entry => entry.Classification.Confidence),
                Reason = group.Last().Classification.Reason
            })
            .OrderByDescending(group => group.Count)
            .ThenByDescending(group => group.Latest)
            .First();

        if ((double)best.Count / _entries.Count < _majorityShare)
            return null;

        return new AttentionClassification(best.State, best.Confidence, best.Reason);
    }

    public void Reset() => _entries.Clear();

    private void Prune(DateTimeOffset now)
    {
        var cutoff = now - _windowDuration;
        _entries.RemoveAll(entry => entry.At < cutoff);
    }

    private readonly record struct Entry(DateTimeOffset At, AttentionClassification Classification);
}
