namespace AttentionPipelineSpike.Metrics;

public sealed record EnvironmentInfo(
    string OsDescription,
    string Framework,
    string ProcessArchitecture,
    int LogicalProcessors,
    string CpuName,
    long ApproximatePhysicalMemoryBytes,
    DateTimeOffset StartedAtUtc);

public sealed record BackendInfo(
    string Name,
    string Model,
    long ModelLoadMs,
    int OnnxThreads,
    float ScoreThreshold,
    float NmsThreshold,
    double LowLightThreshold);

public sealed record InitMetrics(
    double CameraOpenMs,
    double WarmupMs,
    double FirstInferenceMs,
    bool CameraOpened,
    string? CameraError,
    string CaptureApi,
    int RequestedWidth,
    int RequestedHeight);

public sealed record SampleRecord(
    int Index,
    DateTimeOffset TimestampUtc,
    double CaptureMs,
    double DetectMs,
    double TotalMs,
    double Luminance,
    bool FacePresent,
    int FaceCount,
    double Confidence,
    double YawRatio,
    double PitchRatio,
    int EyeCount,
    string RawState,
    string CommittedState,
    string Reason,
    string? Error);

public sealed record ResourceStats(
    double AvgCpuPercentNormalized,
    double MaxCpuPercentNormalized,
    double AvgCpuPercentSingleCore,
    double MaxCpuPercentSingleCore,
    double AvgWorkingSetMb,
    double MaxWorkingSetMb,
    double AvgPrivateMb,
    double MaxPrivateMb);

public sealed record LatencyStats(
    double P50Ms,
    double P95Ms,
    double P99Ms,
    double MaxMs);

public sealed record StabilityStats(
    int PlannedSamples,
    int SuccessfulSamples,
    int FailedSamples,
    int CameraUnavailableSamples,
    int ReopenAttempts,
    int LongestFailureStreak,
    double ActualDurationSeconds);

public sealed record MetricDuration(string Name, double Seconds, double PercentOfKnown);

public sealed record BenchmarkResult(
    EnvironmentInfo Environment,
    BackendInfo Backend,
    InitMetrics Initialization,
    ResourceStats Resources,
    LatencyStats CaptureLatency,
    LatencyStats DetectLatency,
    StabilityStats Stability,
    double LuminanceAvg,
    double LuminanceMin,
    double FaceDetectedPercent,
    IReadOnlyList<MetricDuration> CommittedDurations,
    IReadOnlyList<MetricDuration> RawDurations,
    IReadOnlyList<SampleRecord> Samples,
    IReadOnlyList<string> Notes);

public sealed record BenchmarkOutput(BenchmarkResult Result, string JsonPath, string MarkdownPath);
