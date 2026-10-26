using System.Diagnostics;
using DontBeLazy.Domain.Attention;
using DontBeLazy.Ports.Outbound.Services;

namespace AttentionPipelineSpike.Metrics;

public sealed class BenchmarkRunner
{
    private readonly CliOptions _options;
    private readonly DateTimeOffset _startedAt = DateTimeOffset.UtcNow;

    public BenchmarkRunner(CliOptions options)
    {
        _options = options;
    }

    public async Task<BenchmarkResult> RunAsync(
        string backendName,
        Func<IAttentionFrameAnalyzer> analyzerFactory,
        CancellationToken cancellationToken = default)
    {
        var notes = new List<string>();
        var environment = EnvironmentProbe.Capture() with { StartedAtUtc = _startedAt };

        var analyzerStopwatch = Stopwatch.StartNew();
        using var analyzer = analyzerFactory();
        analyzerStopwatch.Stop();

        notes.Add($"Analyzer init wall time (including model load and JIT): {analyzerStopwatch.Elapsed.TotalMilliseconds:F1} ms; reported model load: {analyzer.ModelLoadMs} ms.");

        var thresholds = new AttentionThresholds(
            LowLightLuminance: _options.LowLightThreshold,
            DistractedAfterSeconds: _options.DistractedAfterSeconds,
            AbsentAfterSeconds: _options.AbsentAfterSeconds,
            LowLightAfterSeconds: _options.LowLightAfterSeconds);

        var openResult = analyzer.Open();
        var cameraError = openResult.Error;
        if (!openResult.Success)
            notes.Add($"Camera open failed: {cameraError}");

        var warmupMs = 0.0;
        var firstInferenceMs = 0.0;
        var firstInferenceMeasured = false;
        var warmupFrames = _options.SkipWarmup ? 0 : _options.WarmupFrames;

        if (openResult.Success)
        {
            warmupMs = Warmup(analyzer, warmupFrames);
            var (ok, inferenceMs, error) = TryFirstInference(analyzer);
            if (ok)
            {
                firstInferenceMs = inferenceMs;
                firstInferenceMeasured = true;
            }
            else if (error != null)
            {
                notes.Add($"Warmup inference failed: {error}");
            }
        }

        if (openResult.Success && warmupFrames > 0)
            notes.Add($"Warmup: {warmupFrames} discarded frame(s) in {warmupMs:F1} ms before sampling.");

        using var resources = new ResourceSampler();
        resources.Start();

        var samples = new List<SampleRecord>();
        var plannedSamples = _options.DurationSeconds <= 0
            ? 1
            : (int)Math.Ceiling(_options.DurationSeconds / _options.IntervalSeconds);

        var stateMachine = new AttentionStateMachine(thresholds);
        var committedDurations = new Dictionary<string, double>();
        var rawDurations = new Dictionary<string, double>();
        var runStopwatch = Stopwatch.StartNew();
        var nextReopenAt = openResult.Success
            ? TimeSpan.Zero
            : TimeSpan.FromSeconds(_options.ReopenSeconds);
        var reopenAttempts = 0;
        var failedSamples = 0;
        var cameraUnavailableSamples = 0;
        var successfulSamples = 0;
        var longestFailureStreak = 0;
        var currentFailureStreak = 0;
        var lastSampleAt = (DateTimeOffset?)null;

        for (var index = 0; index < plannedSamples; index++)
        {
            cancellationToken.ThrowIfCancellationRequested();

            var target = TimeSpan.FromSeconds(index * _options.IntervalSeconds);
            var wait = target - runStopwatch.Elapsed;
            if (wait > TimeSpan.Zero)
                await Task.Delay(wait, cancellationToken);

            if (!analyzer.IsOpen && runStopwatch.Elapsed >= nextReopenAt)
            {
                reopenAttempts++;
                var reopen = analyzer.Open();
                if (reopen.Success)
                {
                    notes.Add($"Camera reopened at sample {index} after {reopen.OpenMs:F1} ms.");
                    cameraError = null;
                }
                else
                {
                    cameraError = reopen.Error;
                    nextReopenAt = runStopwatch.Elapsed + TimeSpan.FromSeconds(_options.ReopenSeconds);
                }
            }

            var sampleStopwatch = Stopwatch.StartNew();
            var frame = analyzer.AnalyzeFrame();

            if (!frame.Captured)
            {
                cameraError = frame.Error ?? cameraError;
                nextReopenAt = runStopwatch.Elapsed + TimeSpan.FromSeconds(_options.ReopenSeconds);
            }

            var error = frame.Error;
            FaceObservation observation;

            if (!frame.Captured)
            {
                observation = FaceObservation.CameraUnavailable;
                failedSamples++;
                cameraUnavailableSamples++;
                currentFailureStreak++;
                longestFailureStreak = Math.Max(longestFailureStreak, currentFailureStreak);
            }
            else if (frame.Face is null)
            {
                observation = FaceObservation.CameraUnavailable;
                error ??= "detector-error";
                failedSamples++;
                currentFailureStreak++;
                longestFailureStreak = Math.Max(longestFailureStreak, currentFailureStreak);
                notes.Add($"Analyzer error at sample {index}: {error}");
            }
            else
            {
                observation = frame.Face;
                successfulSamples++;
                currentFailureStreak = 0;
            }

            if (!firstInferenceMeasured && frame.Captured && frame.Error is null)
            {
                firstInferenceMs = frame.DetectMs;
                firstInferenceMeasured = true;
            }

            var now = DateTimeOffset.UtcNow;
            var classification = AttentionClassifier.Classify(observation, frame.Luminance, thresholds);
            var committed = stateMachine.Update(classification, now);
            var committedName = committed?.ToString() ?? "Unknown";

            if (lastSampleAt is not null)
            {
                var deltaSeconds = Math.Max(0, (now - lastSampleAt.Value).TotalSeconds);
                AddDuration(rawDurations, classification.State.ToString(), deltaSeconds);
                AddDuration(committedDurations, committedName, deltaSeconds);
            }

            lastSampleAt = now;

            samples.Add(new SampleRecord(
                Index: index,
                TimestampUtc: now,
                CaptureMs: frame.CaptureMs,
                DetectMs: frame.DetectMs,
                TotalMs: sampleStopwatch.Elapsed.TotalMilliseconds,
                Luminance: frame.Luminance,
                FacePresent: observation.FacePresent,
                FaceCount: observation.FaceCount,
                Confidence: observation.Confidence,
                YawRatio: observation.HeadYawRatio,
                PitchRatio: observation.HeadPitchRatio,
                EyeCount: observation.EyeCount,
                RawState: classification.State.ToString(),
                CommittedState: committedName,
                Reason: classification.Reason,
                Error: error));
        }

        runStopwatch.Stop();
        var resourceStats = resources.Stop();

        if (samples.Count > 0 && samples.All(s => !s.FacePresent))
            notes.Add("No face was detected in any sample.");

        if (samples.Any(s => s.Reason == "no-face-low-light"))
            notes.Add($"Low-light condition observed (luminance below {_options.LowLightThreshold}).");

        if (samples.Any(s => s.RawState == nameof(RawAttentionState.Distracted)))
            notes.Add("Head-pose heuristic is a Yaw/Pitch proxy; calibration on real users is required.");

        notes.Add("Drowsy state is not evaluated: it requires eye-aspect-ratio landmarks or a dedicated model.");
        notes.Add("Privacy: no frames, landmarks, or embeddings are written to disk; only aggregate metrics and sample rows are persisted.");

        var faceSamples = samples.Where(s => s.Error == null && s.FacePresent).ToList();
        var faceDetectedPercent = successfulSamples == 0 ? 0 : faceSamples.Count * 100.0 / successfulSamples;
        var luminanceValues = samples.Where(s => s.Error == null).Select(s => s.Luminance).ToList();

        var committedTotal = committedDurations.Values.Sum();
        var rawTotal = rawDurations.Values.Sum();

        return new BenchmarkResult(
            Environment: environment,
            Backend: new BackendInfo(
                Name: backendName,
                Model: analyzer.ModelInfo,
                ModelLoadMs: analyzer.ModelLoadMs,
                OnnxThreads: _options.OnnxThreads,
                ScoreThreshold: _options.ScoreThreshold,
                NmsThreshold: _options.NmsThreshold,
                LowLightThreshold: _options.LowLightThreshold),
            Initialization: new InitMetrics(
                CameraOpenMs: openResult.OpenMs,
                WarmupMs: warmupMs,
                FirstInferenceMs: firstInferenceMs,
                CameraOpened: openResult.Success,
                CameraError: openResult.Error,
                CaptureApi: openResult.CaptureApi,
                RequestedWidth: _options.Width,
                RequestedHeight: _options.Height),
            Resources: resourceStats,
            CaptureLatency: ComputeLatency(samples.Where(s => s.Error == null).Select(s => s.CaptureMs)),
            DetectLatency: ComputeLatency(samples.Where(s => s.Error == null).Select(s => s.DetectMs)),
            Stability: new StabilityStats(
                PlannedSamples: plannedSamples,
                SuccessfulSamples: successfulSamples,
                FailedSamples: failedSamples,
                CameraUnavailableSamples: cameraUnavailableSamples,
                ReopenAttempts: reopenAttempts,
                LongestFailureStreak: longestFailureStreak,
                ActualDurationSeconds: samples.Count < 2
                    ? runStopwatch.Elapsed.TotalSeconds
                    : (samples[^1].TimestampUtc - samples[0].TimestampUtc).TotalSeconds),
            LuminanceAvg: luminanceValues.Count == 0 ? 0 : luminanceValues.Average(),
            LuminanceMin: luminanceValues.Count == 0 ? 0 : luminanceValues.Min(),
            FaceDetectedPercent: faceDetectedPercent,
            CommittedDurations: ToDurations(committedDurations, committedTotal),
            RawDurations: ToDurations(rawDurations, rawTotal),
            Samples: samples,
            Notes: notes);
    }

    private static double Warmup(IAttentionFrameAnalyzer analyzer, int frames)
    {
        if (frames <= 0)
            return 0;

        var stopwatch = Stopwatch.StartNew();
        for (var i = 0; i < frames; i++)
        {
            var frame = analyzer.AnalyzeFrame();
            if (!frame.Captured)
                break;
        }

        stopwatch.Stop();
        return stopwatch.Elapsed.TotalMilliseconds;
    }

    private static (bool Ok, double Ms, string? Error) TryFirstInference(IAttentionFrameAnalyzer analyzer)
    {
        var frame = analyzer.AnalyzeFrame();
        if (!frame.Captured)
            return (false, 0, frame.Error);

        if (frame.Error is not null)
            return (false, 0, frame.Error);

        return (true, frame.DetectMs, null);
    }

    private static void AddDuration(Dictionary<string, double> target, string key, double seconds)
    {
        target.TryGetValue(key, out var existing);
        target[key] = existing + seconds;
    }

    private static IReadOnlyList<MetricDuration> ToDurations(Dictionary<string, double> durations, double total)
    {
        return durations
            .OrderByDescending(pair => pair.Value)
            .Select(pair => new MetricDuration(
                pair.Key,
                pair.Value,
                total <= 0 ? 0 : pair.Value / total * 100.0))
            .ToList();
    }

    private static LatencyStats ComputeLatency(IEnumerable<double> values)
    {
        var sorted = values.Where(v => v > 0).OrderBy(v => v).ToList();
        if (sorted.Count == 0)
            return new LatencyStats(0, 0, 0, 0);

        return new LatencyStats(
            Percentile(sorted, 50),
            Percentile(sorted, 95),
            Percentile(sorted, 99),
            sorted[^1]);
    }

    private static double Percentile(List<double> sorted, double percentile)
    {
        var rank = (int)Math.Ceiling(percentile / 100.0 * sorted.Count) - 1;
        return sorted[Math.Clamp(rank, 0, sorted.Count - 1)];
    }
}
