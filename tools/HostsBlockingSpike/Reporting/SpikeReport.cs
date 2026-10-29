using System.Runtime.InteropServices;
using HostsBlockingSpike.BlockPage;
using HostsBlockingSpike.Hosts;
using HostsBlockingSpike.Probes;

namespace HostsBlockingSpike.Reporting;

public sealed class SpikeReport
{
    public DateTimeOffset StartedAt { get; set; } = DateTimeOffset.UtcNow;
    public DateTimeOffset? FinishedAt { get; set; }
    public string CommandLine { get; set; } = string.Empty;
    public string OsDescription { get; set; } = RuntimeInformation.OSDescription;
    public string FrameworkDescription { get; set; } = RuntimeInformation.FrameworkDescription;
    public bool IsElevated { get; set; }
    public bool ApplyHostsRequested { get; set; }
    public string HostsPath { get; set; } = string.Empty;
    public string BackupDir { get; set; } = string.Empty;
    public ProxyProbeResult? Proxy { get; set; }
    public string? Note { get; set; }
    public string? FatalError { get; set; }

    public List<SelfTestResult> SelfTests { get; } = [];
    public List<BlockPageCheck> BlockPageChecks { get; } = [];
    public HostsRecoveryResult? Recovery { get; set; }
    public HostsApplyResult? HostsApply { get; set; }
    public HostsRestoreResult? HostsRestore { get; set; }
    public string HostsSha256Before { get; set; } = string.Empty;
    public string HostsSha256After { get; set; } = string.Empty;
    public PortConflictResult? PortConflict { get; set; }
    public FlushDnsResult? DnsFlush { get; set; }
    public List<DnsProbeResult> DnsResults { get; } = [];
    public List<HttpProbeResult> HttpResults { get; } = [];
    public List<BrowserInfo> Browsers { get; } = [];
    public List<BrowserRunResult> BrowserRuns { get; } = [];
    public List<BlockPageRequest> ServerRequests { get; } = [];
    public List<string> Log { get; } = [];
    public List<string> Limitations { get; } = [];
}

public sealed record BlockPageCheck(
    string Name,
    string HostHeader,
    string Path,
    int StatusCode,
    bool MarkerFound,
    bool NoExternalResources,
    long DurationMs);

public sealed record PortConflictResult(
    bool ConflictDetected,
    string ErrorType,
    string ErrorMessage,
    bool SucceededAfterRelease,
    long DurationMs);
