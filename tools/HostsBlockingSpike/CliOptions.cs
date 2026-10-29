using System.Globalization;

namespace HostsBlockingSpike;

public sealed class CliOptions
{
    public string HostsPath { get; set; } = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.Windows),
        "System32", "drivers", "etc", "hosts");

    public string BackupDir { get; set; } = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.CommonApplicationData),
        "EduFocus", "spike-backup");

    public string OutputDir { get; set; } = "docs/spikes/results";
    public List<string> Domains { get; } = ["example.com", "www.example.com"];
    public int BlockPagePort { get; set; } = 80;
    public int BrowserTimeoutSeconds { get; set; } = 45;
    public bool ApplyHosts { get; set; }
    public bool SkipBrowsers { get; set; }
    public bool SkipSelfTests { get; set; }
    public bool Help { get; set; }

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
                case "--hosts":
                    options.HostsPath = Path.GetFullPath(NextValue());
                    break;
                case "--backup-dir":
                    options.BackupDir = Path.GetFullPath(NextValue());
                    break;
                case "--output-dir":
                    options.OutputDir = Path.GetFullPath(NextValue());
                    break;
                case "--domains":
                    options.Domains.Clear();
                    options.Domains.AddRange(NextValue()
                        .Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries));
                    break;
                case "--port":
                    options.BlockPagePort = int.Parse(NextValue(), CultureInfo.InvariantCulture);
                    break;
                case "--browser-timeout-seconds":
                    options.BrowserTimeoutSeconds = int.Parse(NextValue(), CultureInfo.InvariantCulture);
                    break;
                case "--apply-hosts":
                    options.ApplyHosts = true;
                    break;
                case "--skip-browsers":
                    options.SkipBrowsers = true;
                    break;
                case "--skip-self-tests":
                    options.SkipSelfTests = true;
                    break;
                case "--help":
                case "-h":
                    options.Help = true;
                    break;
                default:
                    throw new ArgumentException($"Unknown argument: {arg}");
            }
        }

        if (options.Domains.Count == 0)
            throw new ArgumentException("At least one domain is required.");
        if (options.BlockPagePort is < 1 or > 65535)
            throw new ArgumentException("--port must be between 1 and 65535.");
        if (options.BrowserTimeoutSeconds < 5)
            throw new ArgumentException("--browser-timeout-seconds must be >= 5.");

        return options;
    }

    public static void PrintHelp()
    {
        Console.WriteLine("""
            Hosts blocking spike (issue #2)

            Usage:
              dotnet run --project tools/HostsBlockingSpike -- [options]

            Options:
              --apply-hosts                  Test the real hosts file (requires elevation).
                                             Without this flag only sandbox/self-tests run.
              --hosts <path>                 Hosts file path (default: %SystemRoot%\System32\drivers\etc\hosts)
              --backup-dir <path>            Backup/journal directory (default: %ProgramData%\EduFocus\spike-backup)
              --output-dir <path>            Report directory (default: docs/spikes/results)
              --domains <list>               Comma-separated test domains (default: example.com,www.example.com)
              --port <n>                     Block page HTTP port (default: 80)
              --browser-timeout-seconds <n>  Per-browser timeout (default: 45)
              --skip-browsers                Skip Chrome/Edge/Firefox probes
              --skip-self-tests              Skip hosts sandbox self-tests
              -h, --help                     Show this help

            Safety: the real hosts file is only touched with --apply-hosts, is backed up,
            is edited only between EDUFOCUS markers, and is restored in a finally block.
            """);
    }
}
