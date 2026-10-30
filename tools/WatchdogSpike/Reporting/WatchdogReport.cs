namespace WatchdogSpike.Reporting;

public sealed class WatchdogReport
{
    public DateTimeOffset StartedAt { get; set; }
    public DateTimeOffset FinishedAt { get; set; }
    public DateTimeOffset ConcludedAt { get; set; }
    public string OsDescription { get; set; } = string.Empty;
    public string FrameworkDescription { get; set; } = string.Empty;
    public bool Elevated { get; set; }
    public string UserName { get; set; } = string.Empty;
    public string WorkRoot { get; set; } = string.Empty;
    public WatchdogConfig Config { get; set; } = new(0, 0, 0, 0, 0, 0, 0);
    public List<PhaseResult> Phases { get; } = [];
    public bool AllPassed { get; set; }
}

public sealed record WatchdogConfig(
    int PollMs,
    int RestartBaseMs,
    int RestartMaxMs,
    int MaxRestarts,
    int ResetSeconds,
    int StaleSeconds,
    int TaskIntervalSeconds);

public sealed record PhaseResult(
    string Name,
    bool Passed,
    string Summary,
    IReadOnlyDictionary<string, string> Metrics,
    IReadOnlyList<AuditEvent> Audit,
    string WorkDir);

public sealed record MechanismRow(
    string Mechanism,
    string Complexity,
    string Privilege,
    string Reliability,
    string Uninstall,
    string Notes);

public sealed record ReportPaths(string MarkdownPath, string JsonPath);
