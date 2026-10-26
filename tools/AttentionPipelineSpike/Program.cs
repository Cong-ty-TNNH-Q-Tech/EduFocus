using System.Diagnostics;
using AttentionPipelineSpike;
using AttentionPipelineSpike.Metrics;
using DontBeLazy.Infrastructure.Attention;
using DontBeLazy.Infrastructure.Attention.Detection;
using OpenCvSharp;

try
{
    var options = CliOptions.Parse(args);
    if (options.Help)
    {
        CliOptions.PrintHelp();
        return 0;
    }

    var backendNames = options.Backends
        .Select(name => name.Trim().ToLowerInvariant())
        .Distinct()
        .ToList();

    if (options.ImagePath is not null)
        return RunImageDiagnostics(options, backendNames);

    var analyzerOptions = CreateAnalyzerOptions(options);
    var exitCode = 0;

    foreach (var backendName in backendNames)
    {
        if (backendName is not ("yunet" or "haar"))
        {
            Console.Error.WriteLine($"Unknown backend: {backendName}. Supported: yunet, haar.");
            exitCode = 1;
            continue;
        }

        Console.WriteLine($"=== Backend: {backendName} ===");
        var runner = new BenchmarkRunner(options);
        var result = await runner.RunAsync(
            backendName,
            () => AttentionAnalyzerFactory.Create(backendName, analyzerOptions, options.ModelDir));
        var output = ResultReporter.Write(result, options.OutputDir);
        PrintSummary(output);
    }

    return exitCode;
}
catch (ArgumentException ex)
{
    Console.Error.WriteLine($"Invalid arguments: {ex.Message}");
    CliOptions.PrintHelp();
    return 1;
}
catch (FileNotFoundException ex)
{
    Console.Error.WriteLine($"Missing model file: {ex.FileName}");
    Console.Error.WriteLine("Run: pwsh tools/AttentionPipelineSpike/fetch-models.ps1");
    return 1;
}
catch (Exception ex)
{
    Console.Error.WriteLine($"Benchmark failed: {ex.GetType().Name}: {ex.Message}");
    Console.Error.WriteLine(ex.StackTrace);
    return 1;
}

static AttentionAnalyzerOptions CreateAnalyzerOptions(CliOptions options) => new(
    CameraIndex: options.CameraIndex,
    CaptureApi: options.CaptureApi,
    Width: options.Width,
    Height: options.Height,
    FlushFrames: options.FlushFrames,
    OnnxThreads: options.OnnxThreads,
    ScoreThreshold: options.ScoreThreshold,
    NmsThreshold: options.NmsThreshold);

int RunImageDiagnostics(CliOptions options, List<string> backendNames)
{
    using var image = Cv2.ImRead(options.ImagePath!);
    if (image.Empty())
    {
        Console.Error.WriteLine($"Could not read image: {options.ImagePath}");
        return 1;
    }

    Console.WriteLine($"Image: {options.ImagePath} ({image.Width}x{image.Height})");
    var analyzerOptions = CreateAnalyzerOptions(options);
    var exitCode = 0;

    foreach (var backendName in backendNames)
    {
        if (backendName is not ("yunet" or "haar"))
        {
            Console.Error.WriteLine($"Unknown backend: {backendName}. Supported: yunet, haar.");
            exitCode = 1;
            continue;
        }

        using var detector = AttentionAnalyzerFactory.CreateDetector(backendName, analyzerOptions, options.ModelDir);
        var observation = detector.Detect(image);
        var timings = new List<double>(options.ImageIterations);
        for (var i = 0; i < options.ImageIterations; i++)
        {
            var stopwatch = Stopwatch.StartNew();
            observation = detector.Detect(image);
            stopwatch.Stop();
            timings.Add(stopwatch.Elapsed.TotalMilliseconds);
        }

        timings.Sort();
        var p50 = timings[(int)Math.Ceiling(0.50 * timings.Count) - 1];
        var p95 = timings[(int)Math.Ceiling(0.95 * timings.Count) - 1];

        Console.WriteLine($"{backendName}: {detector.Model}");
        Console.WriteLine(
            $"  face={observation.FacePresent} count={observation.FaceCount} conf={observation.Confidence:F3} " +
            $"box=({observation.X:F1},{observation.Y:F1},{observation.Width:F1},{observation.Height:F1}) " +
            $"eyes={observation.EyeCount} yaw={observation.HeadYawRatio:F3} pitch={observation.HeadPitchRatio:F3}");
        Console.WriteLine(
            $"  detect over {timings.Count} iteration(s): min={timings[0]:F1} p50={p50:F1} p95={p95:F1} " +
            $"max={timings[^1]:F1} avg={timings.Average():F1} ms");
        if (observation.Nose is { } nose &&
            observation.RightEye is { } rightEye &&
            observation.LeftEye is { } leftEye)
        {
            Console.WriteLine(
                $"  landmarks: rightEye=({rightEye.X:F1},{rightEye.Y:F1}) " +
                $"leftEye=({leftEye.X:F1},{leftEye.Y:F1}) " +
                $"nose=({nose.X:F1},{nose.Y:F1})");
        }
    }

    return exitCode;
}

static void PrintSummary(BenchmarkOutput output)
{
    var result = output.Result;
    Console.WriteLine($"Model: {result.Backend.Model}");
    Console.WriteLine($"Camera: {(result.Initialization.CameraOpened ? "opened" : "unavailable")}" +
                      (result.Initialization.CameraError is null ? "" : $" ({result.Initialization.CameraError})"));
    Console.WriteLine($"Init: camera {result.Initialization.CameraOpenMs:F1} ms, warm-up {result.Initialization.WarmupMs:F1} ms, first inference {result.Initialization.FirstInferenceMs:F1} ms");
    Console.WriteLine($"Latency: capture p95 {result.CaptureLatency.P95Ms:F1} ms, detect p95 {result.DetectLatency.P95Ms:F1} ms");
    Console.WriteLine($"CPU: avg {result.Resources.AvgCpuPercentNormalized:F2}% of machine ({result.Resources.AvgCpuPercentSingleCore:F2}% of one core), max {result.Resources.MaxCpuPercentNormalized:F2}%");
    Console.WriteLine($"Memory: working set avg {result.Resources.AvgWorkingSetMb:F1} MiB, max {result.Resources.MaxWorkingSetMb:F1} MiB");
    Console.WriteLine($"Samples: {result.Stability.SuccessfulSamples}/{result.Stability.PlannedSamples} ok, {result.Stability.FailedSamples} failed, face detected {result.FaceDetectedPercent:F1}%");
    Console.WriteLine("States: " + string.Join(", ",
        result.CommittedDurations.Select(d => $"{d.Name} {d.PercentOfKnown:F1}%")));
    Console.WriteLine($"Reports: {output.MarkdownPath}");
    Console.WriteLine($"         {output.JsonPath}");
    Console.WriteLine();
}
