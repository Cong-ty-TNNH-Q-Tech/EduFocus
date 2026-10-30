using System.Text;
using System.Text.Json;

namespace WatchdogSpike.Reporting;

public sealed class WatchdogReportWriter
{
    private static readonly MechanismRow[] Mechanisms =
    [
        new("Scheduled Task (poll 60 giây)", "Thấp – trung bình", "Task user (không admin) hoặc SYSTEM (admin)",
            "Trung bình", "Xóa task",
            "Trễ 0–60 giây; Task Scheduler repetition tối thiểu 1 phút; task có thể bị disable/xóa; không cần tiến trình thường trú."),
        new("Watchdog process riêng", "Thấp", "Cùng mức app (MVP: elevated)",
            "Trung bình – cao trong user session", "Stop flag + kill",
            "Phát hiện dưới 1 giây; restart có backoff, chống trùng instance; bị kill cùng app nếu không có lớp ngoài."),
        new("Auto-start (Run/Startup/Task at logon)", "Rất thấp", "User",
            "Thấp", "Xóa entry",
            "Chỉ khởi động khi đăng nhập; không phát hiện kill; user có thể tắt trong Task Manager."),
        new("Windows Service", "Cao", "SYSTEM/admin khi cài",
            "Cao", "Uninstall service",
            "SCM recovery; không truy cập webcam/foreground; cần IPC, installer và ACL; Post-MVP.")
    ];

    private static readonly string[] Limitations =
    [
        "Prototype chạy trong user session hiện tại; chưa đăng ký scheduled task thật và chưa đo Task Scheduler. Phase scheduled task chỉ mô phỏng cùng logic với chu kỳ 60 giây.",
        "Chưa kiểm chứng khác biệt giữa standard user và watchdog/app elevated; chưa thử kill bằng Task Manager dưới tài khoản học sinh.",
        "Windows shutdown/logoff, safe mode, admin/SYSTEM và việc gỡ cài đặt có thể dừng mọi cơ chế; không chống được các đường này.",
        "Watchdog process không có lớp ngoài trong prototype; nếu học sinh kill cả app và watchdog thì không có restart cho tới lần đăng nhập sau.",
        "Clean exit phụ thuộc flag file; nếu app crash trước khi ghi flag, watchdog coi là thoát bất thường và restart.",
        "Heartbeat dùng file trên cùng ổ đĩa; disk full, lock hoặc antivirus có thể làm phát hiện sai hoặc trễ.",
        "PID reuse được giảm thiểu bằng so khớp start time với dung sai 2 giây, nhưng chưa test trên máy chạy lâu.",
        "Scheduled task không đảm bảo đúng 60 giây; Task Scheduler có thể trễ khi máy tải cao hoặc đang sleep.",
        "Task-check trong prototype chưa áp dụng restart budget; Phase 7 cần lưu bộ đếm bền vững để tránh restart mỗi phút khi app crash lặp lại.",
        "Chưa đánh giá EDR/antivirus, policy domain hoặc quyền can thiệp của phụ huynh/admin."
    ];

    public ReportPaths Write(WatchdogReport report, string outputDir)
    {
        Directory.CreateDirectory(outputDir);
        var stamp = report.StartedAt.ToString("yyyyMMdd-HHmmss");
        var markdownPath = Path.Combine(outputDir, $"watchdog-{stamp}.md");
        var jsonPath = Path.Combine(outputDir, $"watchdog-{stamp}.json");

        File.WriteAllText(markdownPath, BuildMarkdown(report));
        File.WriteAllText(jsonPath, JsonSerializer.Serialize(report, new JsonSerializerOptions { WriteIndented = true }));
        return new ReportPaths(markdownPath, jsonPath);
    }

    private static string BuildMarkdown(WatchdogReport report)
    {
        var builder = new StringBuilder();
        builder.AppendLine("# Spike watchdog & chống tắt — kết quả thô");
        builder.AppendLine();
        builder.AppendLine("- Issue: [#3](https://github.com/Cong-ty-TNNH-Q-Tech/EduFocus/issues/3)");
        builder.AppendLine($"- Bắt đầu: {report.StartedAt:yyyy-MM-dd HH:mm:ss zzz}");
        builder.AppendLine($"- Kết thúc: {report.FinishedAt:yyyy-MM-dd HH:mm:ss zzz}");
        builder.AppendLine($"- OS: {report.OsDescription}; runtime: {report.FrameworkDescription}");
        builder.AppendLine($"- User: {report.UserName}; elevated: {report.Elevated}");
        builder.AppendLine($"- Work root: `{report.WorkRoot}`");
        builder.AppendLine();

        builder.AppendLine("## 1. Tóm tắt tự động");
        builder.AppendLine();
        builder.AppendLine($"- Phase đạt: {report.Phases.Count(phase => phase.Passed)}/{report.Phases.Count}.");
        builder.AppendLine($"- Kết quả tổng: {(report.AllPassed ? "PASS" : "FAIL")}.");
        builder.AppendLine($"- Scheduled task thật: không đăng ký; phase tương ứng là mô phỏng.");
        builder.AppendLine();

        builder.AppendLine("## 2. Cấu hình");
        builder.AppendLine();
        AppendTable(builder,
            ["Tham số", "Giá trị"],
            [
                ["Poll", $"{report.Config.PollMs} ms"],
                ["Restart backoff", $"{report.Config.RestartBaseMs}–{report.Config.RestartMaxMs} ms"],
                ["Max restarts / cửa sổ", $"{report.Config.MaxRestarts} / {report.Config.ResetSeconds} giây"],
                ["Ngưỡng heartbeat", $"{report.Config.StaleSeconds} giây"],
                ["Chu kỳ mô phỏng task", $"{report.Config.TaskIntervalSeconds} giây"]
            ]);
        builder.AppendLine();

        builder.AppendLine("## 3. Kết quả theo phase");
        builder.AppendLine();
        AppendTable(builder,
            ["Phase", "Kết quả", "Tóm tắt"],
            report.Phases.Select(phase => new[]
            {
                phase.Name,
                phase.Passed ? "pass" : "fail",
                phase.Summary
            }));
        builder.AppendLine();

        foreach (var phase in report.Phases)
        {
            builder.AppendLine($"### 3.{report.Phases.IndexOf(phase) + 1}. {phase.Name}");
            builder.AppendLine();
            builder.AppendLine(phase.Summary);
            builder.AppendLine();
            AppendTable(builder,
                ["Chỉ số", "Giá trị"],
                phase.Metrics.Select(pair => new[] { pair.Key, pair.Value }));
            builder.AppendLine();

            var eventCounts = phase.Audit
                .GroupBy(item => item.Event)
                .OrderBy(group => group.Key)
                .Select(group => new[] { group.Key, group.Count().ToString() })
                .ToArray();
            if (eventCounts.Length > 0)
            {
                builder.AppendLine("Sự kiện audit:");
                builder.AppendLine();
                AppendTable(builder, ["Event", "Số lần"], eventCounts);
                builder.AppendLine();

                var notable = phase.Audit
                    .Where(item => item.Event is not ("target_start" or "target_exit" or "target_clean_exit"))
                    .TakeLast(20)
                    .Select(item => new[]
                    {
                        item.TimestampUtc.ToString("HH:mm:ss.fff"),
                        item.Event,
                        item.Pid?.ToString() ?? "-",
                        item.Attempt?.ToString() ?? "-",
                        item.DelayMs?.ToString() ?? "-",
                        item.Detail ?? "-"
                    })
                    .ToArray();
                if (notable.Length > 0)
                {
                    builder.AppendLine("Sự kiện chính:");
                    builder.AppendLine();
                    AppendTable(builder, ["UTC", "Event", "PID", "Attempt", "Delay ms", "Chi tiết"], notable);
                    builder.AppendLine();
                }
            }
        }

        builder.AppendLine("## 4. So sánh cơ chế (đánh giá định tính)");
        builder.AppendLine();
        AppendTable(builder,
            ["Cơ chế", "Độ phức tạp", "Quyền", "Độ tin cậy", "Gỡ cài đặt", "Ghi chú"],
            Mechanisms.Select(row => new[]
            {
                row.Mechanism, row.Complexity, row.Privilege, row.Reliability, row.Uninstall, row.Notes
            }));
        builder.AppendLine();

        builder.AppendLine("## 5. Giới hạn quan sát");
        builder.AppendLine();
        foreach (var limitation in Limitations)
            builder.AppendLine($"- {limitation}");
        builder.AppendLine();

        builder.AppendLine("## 6. Artifact");
        builder.AppendLine();
        var stamp = report.StartedAt.ToString("yyyyMMdd-HHmmss");
        builder.AppendLine($"- Markdown: `watchdog-{stamp}.md`.");
        builder.AppendLine($"- JSON: `watchdog-{stamp}.json`.");
        builder.AppendLine("- Audit JSONL của từng phase nằm trong work root nếu còn giữ.");
        builder.AppendLine("- Tool: `tools/WatchdogSpike/`.");
        return builder.ToString();
    }

    private static void AppendTable(StringBuilder builder, IReadOnlyList<string> headers,
        IEnumerable<string[]> rows)
    {
        builder.AppendLine("| " + string.Join(" | ", headers) + " |");
        builder.AppendLine("|" + string.Join("|", headers.Select(_ => "---")) + "|");
        foreach (var row in rows)
            builder.AppendLine("| " + string.Join(" | ", row.Select(Escape)) + " |");
    }

    private static string Escape(string value) =>
        value.Replace("|", "\\|").Replace("\r", " ").Replace("\n", " ");
}
