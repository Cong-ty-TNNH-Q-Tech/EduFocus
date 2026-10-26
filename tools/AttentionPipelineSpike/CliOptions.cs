using System.Globalization;

namespace AttentionPipelineSpike;

public sealed class CliOptions
{
    public List<string> Backends { get; } = ["yunet"];
    public int CameraIndex { get; set; }
    public string CaptureApi { get; set; } = "msmf";
    public int Width { get; set; } = 640;
    public int Height { get; set; } = 480;
    public int IntervalSeconds { get; set; } = 15;
    public double DurationMinutes { get; set; } = 30;
    public string OutputDir { get; set; } = "docs/spikes/results";
    public string ModelDir { get; set; } = "tools/AttentionPipelineSpike/models";
    public int WarmupFrames { get; set; } = 5;
    public float ScoreThreshold { get; set; } = 0.6f;
    public float NmsThreshold { get; set; } = 0.3f;
    public double LowLightThreshold { get; set; } = 40;
    public int OnnxThreads { get; set; } = 1;
    public bool SkipWarmup { get; set; }
    public bool Help { get; set; }
    public int ReopenSeconds { get; set; } = 30;
    public int FlushFrames { get; set; } = 1;
    public double DistractedAfterSeconds { get; set; } = 20;
    public double AbsentAfterSeconds { get; set; } = 30;
    public double LowLightAfterSeconds { get; set; } = 30;
    public string? ImagePath { get; set; }
    public int ImageIterations { get; set; } = 1;

    public double DurationSeconds => DurationMinutes * 60;

    public static CliOptions Parse(string[] args)
    {
        var options = new CliOptions();

        for (var i = 0; i < args.Length; i++)
        {
            var arg = args[i];
            string NextValue()
            {
                if (i + 1 >= args.Length)
                    throw new ArgumentException($"Missing value for {arg}");
                return args[++i];
            }

            switch (arg)
            {
                case "--backends":
                    options.Backends.Clear();
                    options.Backends.AddRange(NextValue().Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries));
                    break;
                case "--camera":
                    options.CameraIndex = int.Parse(NextValue(), CultureInfo.InvariantCulture);
                    break;
                case "--capture":
                    options.CaptureApi = NextValue().ToLowerInvariant();
                    break;
                case "--width":
                    options.Width = int.Parse(NextValue(), CultureInfo.InvariantCulture);
                    break;
                case "--height":
                    options.Height = int.Parse(NextValue(), CultureInfo.InvariantCulture);
                    break;
                case "--interval-seconds":
                    options.IntervalSeconds = int.Parse(NextValue(), CultureInfo.InvariantCulture);
                    break;
                case "--duration-minutes":
                    options.DurationMinutes = double.Parse(NextValue(), CultureInfo.InvariantCulture);
                    break;
                case "--output-dir":
                    options.OutputDir = NextValue();
                    break;
                case "--model-dir":
                    options.ModelDir = NextValue();
                    break;
                case "--warmup-frames":
                    options.WarmupFrames = int.Parse(NextValue(), CultureInfo.InvariantCulture);
                    break;
                case "--score-threshold":
                    options.ScoreThreshold = float.Parse(NextValue(), CultureInfo.InvariantCulture);
                    break;
                case "--nms-threshold":
                    options.NmsThreshold = float.Parse(NextValue(), CultureInfo.InvariantCulture);
                    break;
                case "--low-light-threshold":
                    options.LowLightThreshold = double.Parse(NextValue(), CultureInfo.InvariantCulture);
                    break;
                case "--onnx-threads":
                    options.OnnxThreads = int.Parse(NextValue(), CultureInfo.InvariantCulture);
                    break;
                case "--reopen-seconds":
                    options.ReopenSeconds = int.Parse(NextValue(), CultureInfo.InvariantCulture);
                    break;
                case "--flush-frames":
                    options.FlushFrames = int.Parse(NextValue(), CultureInfo.InvariantCulture);
                    break;
                case "--distracted-seconds":
                    options.DistractedAfterSeconds = double.Parse(NextValue(), CultureInfo.InvariantCulture);
                    break;
                case "--absent-seconds":
                    options.AbsentAfterSeconds = double.Parse(NextValue(), CultureInfo.InvariantCulture);
                    break;
                case "--low-light-seconds":
                    options.LowLightAfterSeconds = double.Parse(NextValue(), CultureInfo.InvariantCulture);
                    break;
                case "--image":
                    options.ImagePath = NextValue();
                    break;
                case "--image-iterations":
                    options.ImageIterations = int.Parse(NextValue(), CultureInfo.InvariantCulture);
                    break;
                case "--skip-warmup":
                    options.SkipWarmup = true;
                    break;
                case "--help":
                case "-h":
                    options.Help = true;
                    break;
                default:
                    throw new ArgumentException($"Unknown argument: {arg}");
            }
        }

        if (options.Backends.Count == 0)
            throw new ArgumentException("At least one backend is required.");
        if (options.IntervalSeconds < 1)
            throw new ArgumentException("--interval-seconds must be >= 1.");
        if (options.DurationMinutes < 0)
            throw new ArgumentException("--duration-minutes must be >= 0.");
        if (options.ReopenSeconds < 1)
            throw new ArgumentException("--reopen-seconds must be >= 1.");
        if (options.FlushFrames < 0)
            throw new ArgumentException("--flush-frames must be >= 0.");
        if (options.OnnxThreads < 1)
            throw new ArgumentException("--onnx-threads must be >= 1.");
        if (options.ImageIterations < 1)
            throw new ArgumentException("--image-iterations must be >= 1.");

        return options;
    }

    public static void PrintHelp()
    {
        Console.WriteLine("""
            Attention pipeline spike

            Usage:
              dotnet run --project tools/AttentionPipelineSpike -- [options]

            Options:
              --backends <list>            Comma-separated: yunet,haar (default: yunet)
              --camera <index>             Camera index (default: 0)
              --capture <msmf|dshow>       OpenCV capture API (default: msmf)
              --width <px>                 Capture width (default: 640)
              --height <px>                Capture height (default: 480)
              --interval-seconds <n>       Sampling interval (default: 15)
              --duration-minutes <n>       Benchmark duration (default: 30)
              --output-dir <path>          Report output directory (default: docs/spikes/results)
              --model-dir <path>           Model directory (default: tools/AttentionPipelineSpike/models)
              --warmup-frames <n>          Frames before measurement (default: 5)
              --score-threshold <0..1>     Face confidence threshold (default: 0.6)
              --nms-threshold <0..1>       NMS IoU threshold (default: 0.3)
              --low-light-threshold <0..255> Mean luminance threshold (default: 40)
              --onnx-threads <n>           ONNX intra-op threads (default: 1)
              --reopen-seconds <n>         Retry camera open interval (default: 30)
              --flush-frames <n>           Frames discarded before each read (default: 1)
              --distracted-seconds <n>     Distracted debounce (default: 20)
              --absent-seconds <n>         Absent debounce (default: 30)
              --low-light-seconds <n>      Low-light debounce (default: 30)
              --image <path>               Diagnostic: run detector on one still image, no report
              --image-iterations <n>       Repeated detections for latency sampling (default: 1)
              --skip-warmup                Skip warm-up inference
              -h, --help                   Show this help

            Privacy: frames are processed in memory only and disposed immediately.
            No image, landmark or embedding data is written to disk or network.
            """);
    }
}
