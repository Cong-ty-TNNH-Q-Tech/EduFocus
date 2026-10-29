namespace EduFocus.Infrastructure.Attention;

/// <summary>
/// Inference backend settings. Camera capture is owned by
/// <c>ICameraSessionUseCase</c> since issue #30; these values only tune the
/// detector.
/// </summary>
public sealed record AttentionAnalyzerOptions(
    int OnnxThreads = 1,
    float ScoreThreshold = 0.6f,
    float NmsThreshold = 0.3f);
