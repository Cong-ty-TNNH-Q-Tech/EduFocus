namespace EduFocus.UseCases.Attention;

/// <summary>
/// Camera supervision limits (UC08, issue #30). Open/capture calls are wrapped
/// in a timeout because capture backends can block on a removed or wedged
/// device; retries start at 30 seconds and back off to the five-minute cap.
/// </summary>
public sealed class CameraOptions
{
    public static readonly TimeSpan DefaultOperationTimeout = TimeSpan.FromSeconds(5);
    public static readonly TimeSpan DefaultInitialRetryDelay = TimeSpan.FromSeconds(30);
    public static readonly TimeSpan DefaultMaxRetryDelay = TimeSpan.FromMinutes(5);

    public const int DefaultMaxConsecutiveCaptureFailures = 3;

    public TimeSpan OperationTimeout { get; init; } = DefaultOperationTimeout;

    public TimeSpan InitialRetryDelay { get; init; } = DefaultInitialRetryDelay;

    public TimeSpan MaxRetryDelay { get; init; } = DefaultMaxRetryDelay;

    public int MaxConsecutiveCaptureFailures { get; init; } = DefaultMaxConsecutiveCaptureFailures;
}
