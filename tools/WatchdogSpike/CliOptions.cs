using System.Globalization;

namespace WatchdogSpike;

public enum SpikeCommand
{
    Scenario,
    Watchdog,
    Target,
    TaskCheck,
    TaskCheckLoop
}

public sealed class CliOptions
{
    public SpikeCommand Command { get; set; } = SpikeCommand.Scenario;
    public string WorkDir { get; set; } = string.Empty;
    public string OutputDir { get; set; } = "docs/spikes/results";
    public int PollMs { get; set; } = 200;
    public int RestartBaseMs { get; set; } = 1000;
    public int RestartMaxMs { get; set; } = 4000;
    public int MaxRestarts { get; set; } = 5;
    public int ResetSeconds { get; set; } = 600;
    public int StaleSeconds { get; set; } = 3;
    public int GracefulStopMs { get; set; } = 3000;
    public int HeartbeatMs { get; set; } = 300;
    public int TaskIntervalSeconds { get; set; } = 60;
    public bool SkipTaskSimulation { get; set; }
    public bool KeepWorkDir { get; set; }
    public bool Help { get; set; }

    public static CliOptions Parse(string[] args)
    {
        var options = new CliOptions();
        if (args.Length == 0 || args[0] is "--help" or "-h")
        {
            options.Help = true;
            return options;
        }

        options.Command = args[0].ToLowerInvariant() switch
        {
            "scenario" => SpikeCommand.Scenario,
            "watchdog" => SpikeCommand.Watchdog,
            "target" => SpikeCommand.Target,
            "task-check" => SpikeCommand.TaskCheck,
            "task-check-loop" => SpikeCommand.TaskCheckLoop,
            _ => throw new ArgumentException($"Unknown command: {args[0]}")
        };

        for (var i = 1; i < args.Length; i++)
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
                case "--work-dir":
                    options.WorkDir = Path.GetFullPath(NextValue());
                    break;
                case "--output-dir":
                    options.OutputDir = Path.GetFullPath(NextValue());
                    break;
                case "--poll-ms":
                    options.PollMs = ParseInt(NextValue(), arg);
                    break;
                case "--restart-base-ms":
                    options.RestartBaseMs = ParseInt(NextValue(), arg);
                    break;
                case "--restart-max-ms":
                    options.RestartMaxMs = ParseInt(NextValue(), arg);
                    break;
                case "--max-restarts":
                    options.MaxRestarts = ParseInt(NextValue(), arg);
                    break;
                case "--reset-seconds":
                    options.ResetSeconds = ParseInt(NextValue(), arg);
                    break;
                case "--stale-seconds":
                    options.StaleSeconds = ParseInt(NextValue(), arg);
                    break;
                case "--graceful-stop-ms":
                    options.GracefulStopMs = ParseInt(NextValue(), arg);
                    break;
                case "--heartbeat-ms":
                    options.HeartbeatMs = ParseInt(NextValue(), arg);
                    break;
                case "--task-interval-seconds":
                    options.TaskIntervalSeconds = ParseInt(NextValue(), arg);
                    break;
                case "--skip-task-simulation":
                    options.SkipTaskSimulation = true;
                    break;
                case "--keep-work-dir":
                    options.KeepWorkDir = true;
                    break;
                case "--help":
                case "-h":
                    options.Help = true;
                    break;
                default:
                    throw new ArgumentException($"Unknown argument: {arg}");
            }
        }

        if (options.Command != SpikeCommand.Scenario && string.IsNullOrWhiteSpace(options.WorkDir))
            throw new ArgumentException("--work-dir is required for internal commands.");
        if (options.PollMs < 50)
            throw new ArgumentException("--poll-ms must be >= 50.");
        if (options.RestartBaseMs < 100)
            throw new ArgumentException("--restart-base-ms must be >= 100.");
        if (options.RestartMaxMs < options.RestartBaseMs)
            throw new ArgumentException("--restart-max-ms must be >= --restart-base-ms.");
        if (options.MaxRestarts < 1)
            throw new ArgumentException("--max-restarts must be >= 1.");
        if (options.StaleSeconds < 1)
            throw new ArgumentException("--stale-seconds must be >= 1.");
        if (options.HeartbeatMs < 50)
            throw new ArgumentException("--heartbeat-ms must be >= 50.");
        if (options.TaskIntervalSeconds < 1)
            throw new ArgumentException("--task-interval-seconds must be >= 1.");

        return options;
    }

    private static int ParseInt(string value, string arg)
    {
        var parsed = int.Parse(value, CultureInfo.InvariantCulture);
        if (parsed < 0)
            throw new ArgumentException($"Invalid value for {arg}: {value}");
        return parsed;
    }

    public static void PrintHelp()
    {
        Console.WriteLine("""
            Watchdog spike

            Usage:
              dotnet run --project tools/WatchdogSpike -c Release -- scenario [options]
              dotnet run --project tools/WatchdogSpike -c Release -- watchdog --work-dir <path> [options]

            Commands:
              scenario         Chạy ma trận kiểm chứng và ghi report
              watchdog         Tiến trình watchdog (internal)
              target           Ứng dụng giả lập (internal)
              task-check       Một lần kiểm tra kiểu scheduled task (internal)
              task-check-loop  Mô phỏng scheduled task theo chu kỳ (internal)

            Options:
              --work-dir <path>             Thư mục trạng thái cho lệnh internal
              --output-dir <path>           Thư mục report (default: docs/spikes/results)
              --poll-ms <n>                 Chu kỳ watchdog (default: 200)
              --restart-base-ms <n>         Backoff cơ sở (default: 1000)
              --restart-max-ms <n>          Backoff tối đa (default: 4000)
              --max-restarts <n>            Số lần restart tối đa trong cửa sổ (default: 5)
              --reset-seconds <n>           Thời gian ổn định để reset backoff (default: 600)
              --stale-seconds <n>           Ngưỡng heartbeat cũ để coi là treo (default: 3)
              --graceful-stop-ms <n>        Chờ app thoát êm khi dừng (default: 3000)
              --heartbeat-ms <n>            Chu kỳ heartbeat của target (default: 300)
              --task-interval-seconds <n>   Chu kỳ mô phỏng scheduled task (default: 60)
              --skip-task-simulation        Bỏ phase mô phỏng scheduled task
              --keep-work-dir               Giữ thư mục trạng thái sau khi chạy
              -h, --help                    Hiển thị trợ giúp

            Privacy: chỉ ghi PID, timestamp và sự kiện watchdog; không đọc dữ liệu người dùng.
            """);
    }
}
