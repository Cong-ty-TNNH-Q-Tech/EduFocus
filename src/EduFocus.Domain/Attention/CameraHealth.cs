namespace EduFocus.Domain.Attention;

/// <summary>Outcome of the short camera check/calibration (UC08, issue #34).</summary>
public enum CameraHealth
{
    Ready,
    TooDark,
    NoFace,
    NoImage,
    Unavailable
}

/// <summary>
/// Why the camera is unavailable. Kept separate from the capture adapter enum so
/// the guidance policy stays in the domain and does not depend on OpenCV.
/// </summary>
public enum CameraHealthProblem
{
    None,
    Busy,
    PermissionDenied,
    NoCamera,
    Disconnected,
    Timeout,
    Unknown,
    MonitoringActive
}

/// <summary>One analyzed frame reduced to the signals calibration needs.</summary>
public readonly record struct CameraCheckReading(double Luminance, bool FacePresent);

public sealed record CameraCheckThresholds(
    double LowLightLuminance = 40,
    double MinimumLitFrameRatio = 0.6);

/// <summary>
/// Aggregated calibration result. Percentages and raw frames are not retained;
/// only counts and the mean luminance are kept long enough to guide the parent.
/// </summary>
public sealed record CameraHealthResult(
    CameraHealth Health,
    CameraHealthProblem Problem,
    int AttemptedFrames,
    int AnalyzedFrames,
    int FaceFrames,
    double? MeanLuminance)
{
    public bool IsReady => Health == CameraHealth.Ready;
}

public static class CameraHealthEvaluator
{
    public static CameraHealthResult Evaluate(
        CameraHealthProblem problem,
        IReadOnlyList<CameraCheckReading> readings,
        int attemptedFrames,
        CameraCheckThresholds thresholds)
    {
        ArgumentNullException.ThrowIfNull(readings);
        ArgumentNullException.ThrowIfNull(thresholds);

        if (attemptedFrames < 0)
            throw new ArgumentOutOfRangeException(nameof(attemptedFrames), attemptedFrames, "Attempted frames cannot be negative.");
        if (thresholds.LowLightLuminance is < 0 or > 255)
            throw new ArgumentOutOfRangeException(nameof(thresholds), "The low-light luminance must be between 0 and 255.");
        if (thresholds.MinimumLitFrameRatio is < 0 or > 1)
            throw new ArgumentOutOfRangeException(nameof(thresholds), "The lit-frame ratio must be between 0 and 1.");

        if (problem != CameraHealthProblem.None)
            return Build(CameraHealth.Unavailable, problem, attemptedFrames, readings);

        if (readings.Count == 0)
            return Build(CameraHealth.NoImage, CameraHealthProblem.None, attemptedFrames, readings);

        var meanLuminance = readings.Average(reading => reading.Luminance);
        var litFrames = readings.Count(reading => reading.Luminance >= thresholds.LowLightLuminance);

        if (meanLuminance < thresholds.LowLightLuminance
            || litFrames < readings.Count * thresholds.MinimumLitFrameRatio)
        {
            return Build(CameraHealth.TooDark, CameraHealthProblem.None, attemptedFrames, readings);
        }

        if (readings.All(reading => !reading.FacePresent))
            return Build(CameraHealth.NoFace, CameraHealthProblem.None, attemptedFrames, readings);

        return Build(CameraHealth.Ready, CameraHealthProblem.None, attemptedFrames, readings);
    }

    private static CameraHealthResult Build(
        CameraHealth health,
        CameraHealthProblem problem,
        int attemptedFrames,
        IReadOnlyList<CameraCheckReading> readings) =>
        new(
            health,
            problem,
            attemptedFrames,
            readings.Count,
            readings.Count(reading => reading.FacePresent),
            readings.Count == 0 ? null : readings.Average(reading => reading.Luminance));
}
