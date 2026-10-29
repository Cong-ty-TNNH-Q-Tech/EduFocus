using System.Globalization;
using System.Text;
using System.Text.Json;

namespace HostsBlockingSpike.Reporting;

public sealed record ReportPaths(string MarkdownPath, string JsonPath, string LogPath);

public static class ReportWriter
{
    public static ReportPaths Write(SpikeReport report, string outputDir)
    {
        Directory.CreateDirectory(outputDir);
        var baseName = $"hosts-{report.StartedAt.ToLocalTime().ToString("yyyyMMdd-HHmmss", CultureInfo.InvariantCulture)}";
        var markdownPath = Path.Combine(outputDir, baseName + ".md");
        var jsonPath = Path.Combine(outputDir, baseName + ".json");
        var logPath = Path.Combine(outputDir, baseName + ".log");

        File.WriteAllText(markdownPath, BuildMarkdown(report), new UTF8Encoding(false));
        File.WriteAllText(
            jsonPath,
            JsonSerializer.Serialize(report, new JsonSerializerOptions { WriteIndented = true }),
            new UTF8Encoding(false));
        File.WriteAllText(logPath, string.Join(Environment.NewLine, report.Log), new UTF8Encoding(false));

        return new ReportPaths(markdownPath, jsonPath, logPath);
    }

    private static string BuildMarkdown(SpikeReport report)
    {
        var builder = new StringBuilder();
        var selfTestsPassed = report.SelfTests.Count(test => test.Passed);
        var blockChecksPassed = report.BlockPageChecks.Count(IsBlockPageCheckPassed);
        var browserHttpRuns = report.BrowserRuns.Where(run => run.Mode == "http").ToList();
        var browserHttpsRuns = report.BrowserRuns.Where(run => run.Mode == "https").ToList();

        builder.AppendLine("# Spike hosts file & block page — kết quả thô");
        builder.AppendLine();
        builder.AppendLine("- Issue: [#2](https://github.com/Cong-ty-TNNH-Q-Tech/EduFocus/issues/2)");
        builder.AppendLine($"- Bắt đầu: {FormatTime(report.StartedAt)}");
        builder.AppendLine($"- Kết thúc: {FormatTime(report.FinishedAt)}");
        builder.AppendLine($"- Lệnh: `{report.CommandLine}`");
        builder.AppendLine($"- Elevated: {report.IsElevated}; yêu cầu hosts thật: {report.ApplyHostsRequested}");
        builder.AppendLine($"- OS: {report.OsDescription}; runtime: {report.FrameworkDescription}");
        builder.AppendLine($"- Hosts: `{report.HostsPath}`");
        builder.AppendLine($"- Backup/journal: `{report.BackupDir}`");
        builder.AppendLine($"- Proxy hệ thống: `{report.Proxy?.EffectiveProxy ?? "(unknown)"}` (usesProxy={report.Proxy?.UsesProxy})");
        if (!string.IsNullOrWhiteSpace(report.Note))
            builder.AppendLine($"- Ghi chú: {report.Note}");
        builder.AppendLine();

        builder.AppendLine("## Tóm tắt tự động");
        builder.AppendLine();
        builder.AppendLine($"- Self-test hosts: {selfTestsPassed}/{report.SelfTests.Count} đạt.");
        builder.AppendLine($"- Kiểm tra block page loopback: {blockChecksPassed}/{report.BlockPageChecks.Count} đạt.");
        builder.AppendLine(report.HostsApply is null
            ? "- Hosts thật: không chạy."
            : $"- Hosts thật: {report.HostsApply.Domains.Count} domain, changed={report.HostsApply.Changed}, restoreExact={report.HostsRestore?.RestoredExact}.");
        builder.AppendLine(report.BrowserRuns.Count == 0
            ? "- Trình duyệt: không chạy."
            : $"- Trình duyệt: {report.BrowserRuns.Count} lần chạy (HTTP {browserHttpRuns.Count}, HTTPS {browserHttpsRuns.Count}).");
        if (report.BrowserRuns.Count > 0)
        {
            var httpEvidence = report.BrowserRuns
                .Where(run => run.Mode == "http")
                .GroupBy(run => run.Browser)
                .Select(group =>
                    $"{group.Key}={(group.Any(run => run.BlockPageInDom == true || run.BlockPageServerHit) ? "yes" : "no")}");
            builder.AppendLine($"- Block page HTTP theo trình duyệt: {string.Join(", ", httpEvidence)}.");
        }
        if (!string.IsNullOrWhiteSpace(report.FatalError))
            builder.AppendLine($"- Lỗi: {report.FatalError}");
        builder.AppendLine();

        builder.AppendLine("## 1. Self-test hosts (sandbox)");
        builder.AppendLine();
        if (report.SelfTests.Count == 0)
        {
            builder.AppendLine("Không chạy.");
        }
        else
        {
            AppendTable(
                builder,
                ["Test", "Kết quả", "Chi tiết", "ms"],
                report.SelfTests.Select(test => new[]
                {
                    test.Name,
                    test.Passed ? "pass" : "fail",
                    test.Detail,
                    test.DurationMs.ToString(CultureInfo.InvariantCulture)
                }));
        }

        builder.AppendLine("## 2. Block page loopback (port tạm)");
        builder.AppendLine();
        if (report.BlockPageChecks.Count == 0)
        {
            builder.AppendLine("Không chạy.");
        }
        else
        {
            AppendTable(
                builder,
                ["Kiểm tra", "Host header", "Path", "HTTP", "Marker", "Không tài nguyên ngoài", "ms"],
                report.BlockPageChecks.Select(check => new[]
                {
                    check.Name,
                    check.HostHeader,
                    check.Path,
                    check.StatusCode.ToString(CultureInfo.InvariantCulture),
                    check.MarkerFound ? "yes" : "no",
                    check.NoExternalResources ? "yes" : "no",
                    check.DurationMs.ToString(CultureInfo.InvariantCulture)
                }));
        }

        builder.AppendLine("## 3. Hosts thật: apply / restore");
        builder.AppendLine();
        if (report.HostsApply is null)
        {
            builder.AppendLine("Không chạy (thiếu `--apply-hosts` hoặc không elevated).");
            builder.AppendLine();
        }
        else
        {
            AppendTable(
                builder,
                ["Hạng mục", "Giá trị"],
                [
                    ["Backup", report.HostsApply.BackupPath],
                    ["Domains", string.Join(", ", report.HostsApply.Domains)],
                    ["Block lines", report.HostsApply.BlockLineCount.ToString(CultureInfo.InvariantCulture)],
                    ["Changed", report.HostsApply.Changed.ToString()],
                    ["SHA-256 trước", ShortHash(report.HostsApply.OriginalSha256)],
                    ["SHA-256 sau apply", ShortHash(report.HostsApply.UpdatedSha256)],
                    ["SHA-256 sau restore", ShortHash(report.HostsRestore?.FinalSha256 ?? string.Empty)],
                    ["Restore xóa block", report.HostsRestore?.BlockRemoved.ToString() ?? "n/a"],
                    ["Restore exact", report.HostsRestore?.RestoredExact.ToString() ?? "n/a"],
                    ["Pre-run recovery", report.Recovery?.Detail ?? "n/a"],
                    ["Hash trước == sau", string.Equals(report.HostsSha256Before, report.HostsSha256After, StringComparison.Ordinal).ToString()]
                ]);
        }

        builder.AppendLine("## 4. Cổng block page bị chiếm");
        builder.AppendLine();
        if (report.PortConflict is null)
        {
            builder.AppendLine("Không chạy.");
            builder.AppendLine();
        }
        else
        {
            AppendTable(
                builder,
                ["Phát hiện conflict", "Loại lỗi", "Thông điệp", "Chạy lại sau khi nhả cổng", "ms"],
                [
                    [
                        report.PortConflict.ConflictDetected.ToString(),
                        report.PortConflict.ErrorType,
                        report.PortConflict.ErrorMessage,
                        report.PortConflict.SucceededAfterRelease.ToString(),
                        report.PortConflict.DurationMs.ToString(CultureInfo.InvariantCulture)
                    ]
                ]);
        }

        builder.AppendLine("## 5. DNS, HTTP và HTTPS");
        builder.AppendLine();
        if (report.DnsFlush is not null)
            builder.AppendLine($"- Flush DNS sau apply: success={report.DnsFlush.Success}, exit={report.DnsFlush.ExitCode}, {report.DnsFlush.DurationMs} ms.");
        builder.AppendLine();
        if (report.DnsResults.Count == 0 && report.HttpResults.Count == 0)
        {
            builder.AppendLine("Không chạy.");
            builder.AppendLine();
        }
        if (report.DnsResults.Count > 0)
        {
            AppendTable(
                builder,
                ["Host", "Resolve được", "Địa chỉ", "ms"],
                report.DnsResults.Select(dns => new[]
                {
                    dns.Host,
                    dns.Resolved ? "yes" : "no",
                    string.Join(", ", dns.Addresses),
                    dns.DurationMs.ToString(CultureInfo.InvariantCulture)
                }));
        }

        if (report.HttpResults.Count > 0)
        {
            AppendTable(
                builder,
                ["URL", "Mode", "Thành công", "HTTP", "Block page", "Lỗi", "ms"],
                report.HttpResults.Select(http => new[]
                {
                    http.Url,
                    http.Mode,
                    http.Success ? "yes" : "no",
                    http.StatusCode?.ToString(CultureInfo.InvariantCulture) ?? "-",
                    http.BlockPageMarker ? "yes" : "no",
                    http.ErrorType is null ? "-" : http.ErrorType,
                    http.DurationMs.ToString(CultureInfo.InvariantCulture)
                }));
        }

        builder.AppendLine("## 6. Trình duyệt");
        builder.AppendLine();
        if (report.Browsers.Count == 0)
        {
            builder.AppendLine("Không tìm thấy Chrome/Edge/Firefox.");
            builder.AppendLine();
        }
        else
        {
            AppendTable(
                builder,
                ["Trình duyệt", "Phiên bản", "Đường dẫn"],
                report.Browsers.Select(browser => new[]
                {
                    browser.Name,
                    browser.Version ?? "?",
                    browser.ExecutablePath
                }));
        }

        if (report.BrowserRuns.Count > 0)
        {
            AppendTable(
                builder,
                ["Trình duyệt", "Mode", "URL", "Exit", "Timeout", "DOM marker", "Server hit", "UA khớp", "Screenshot", "ms"],
                report.BrowserRuns.Select(run => new[]
                {
                    run.Browser,
                    run.Mode,
                    run.Url,
                    run.ExitCode?.ToString(CultureInfo.InvariantCulture) ?? "-",
                    run.TimedOut ? "yes" : "no",
                    run.BlockPageInDom?.ToString() ?? "n/a",
                    run.BlockPageServerHit ? "yes" : "no",
                    run.ServerHitUserAgent ?? "-",
                    run.ScreenshotPath is null ? "-" : Path.GetFileName(run.ScreenshotPath),
                    run.DurationMs.ToString(CultureInfo.InvariantCulture)
                }));
        }

        builder.AppendLine("## 7. Request mà block page server nhận được");
        builder.AppendLine();
        if (report.ServerRequests.Count == 0)
        {
            builder.AppendLine("Không có request.");
            builder.AppendLine();
        }
        else
        {
            AppendTable(
                builder,
                ["Thời điểm UTC", "Method", "Host", "Target", "UA", "HTTP"],
                report.ServerRequests.Select(request => new[]
                {
                    request.TimestampUtc.ToString("HH:mm:ss.fff", CultureInfo.InvariantCulture),
                    request.Method,
                    request.Host,
                    request.Target,
                    request.UserAgent ?? "-",
                    request.StatusCode.ToString(CultureInfo.InvariantCulture)
                }));
        }

        builder.AppendLine("## 8. Giới hạn quan sát");
        builder.AppendLine();
        foreach (var limitation in report.Limitations)
            builder.AppendLine($"- {limitation}");
        builder.AppendLine();

        builder.AppendLine("## 9. Artifact");
        builder.AppendLine();
        var baseName = report.StartedAt.ToLocalTime().ToString("yyyyMMdd-HHmmss", CultureInfo.InvariantCulture);
        builder.AppendLine($"- Report: `hosts-{baseName}.md`.");
        builder.AppendLine($"- JSON: `hosts-{baseName}.json`.");
        builder.AppendLine($"- Log: `hosts-{baseName}.log`.");
        builder.AppendLine("- Screenshot Firefox (nếu có) nằm cùng thư mục report.");

        return builder.ToString();
    }

    private static bool IsBlockPageCheckPassed(BlockPageCheck check)
    {
        var expectsBlock = !check.HostHeader.Contains("not-blocked", StringComparison.OrdinalIgnoreCase) &&
                           !check.Name.Contains("escape", StringComparison.OrdinalIgnoreCase);
        if (check.Name.Equals("HTML escape", StringComparison.OrdinalIgnoreCase))
            return check.MarkerFound && check.NoExternalResources;

        return expectsBlock
            ? check.StatusCode == 200 && check.MarkerFound && check.NoExternalResources
            : check.StatusCode == 404 && !check.MarkerFound;
    }

    private static void AppendTable(StringBuilder builder, string[] headers, IEnumerable<string[]> rows)
    {
        builder.AppendLine("| " + string.Join(" | ", headers.Select(Escape)) + " |");
        builder.AppendLine("|" + string.Join("|", headers.Select(_ => "---")) + "|");
        foreach (var row in rows)
            builder.AppendLine("| " + string.Join(" | ", row.Select(Escape)) + " |");
        builder.AppendLine();
    }

    private static string Escape(string value) =>
        (value ?? string.Empty).Replace("|", "\\|").Replace("\r", " ").Replace("\n", " ");

    private static string FormatTime(DateTimeOffset? value) =>
        value?.ToLocalTime().ToString("yyyy-MM-dd HH:mm:ss zzz", CultureInfo.InvariantCulture) ?? "-";

    private static string ShortHash(string hash) =>
        string.IsNullOrEmpty(hash) ? "-" : (hash.Length <= 16 ? hash : hash[..16] + "…");
}
