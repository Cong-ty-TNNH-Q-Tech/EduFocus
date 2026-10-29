using EduFocus.Domain.Attention;

namespace EduFocus.UseCases.Attention;

/// <summary>
/// Camera check limits (UC08, issue #34). The check is intentionally short: it
/// only needs enough frames to tell a working camera from an empty, dark or
/// contended one.
/// </summary>
public sealed class CameraCheckOptions
{
    public const int DefaultSampleCount = 5;
    public const int MinSampleCount = 1;
    public const int MaxSampleCount = 10;

    public static readonly TimeSpan DefaultSampleInterval = TimeSpan.FromMilliseconds(250);
    public static readonly TimeSpan MinSampleInterval = TimeSpan.FromMilliseconds(50);
    public static readonly TimeSpan MaxSampleInterval = TimeSpan.FromSeconds(2);

    public int SampleCount { get; init; } = DefaultSampleCount;

    public TimeSpan SampleInterval { get; init; } = DefaultSampleInterval;

    public CameraCheckThresholds Thresholds { get; init; } = new();
}
