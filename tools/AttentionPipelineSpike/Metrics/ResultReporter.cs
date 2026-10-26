using System.Globalization;
using System.Text;
using System.Text.Json;

namespace AttentionPipelineSpike.Metrics;

public static class ResultReporter
{
    public static BenchmarkOutput Write(BenchmarkResult result, string outputDir)
    {
        Directory.CreateDirectory(outputDir);
        var stamp = result.Environment.StartedAtUtc.ToLocalTime().ToString("yyyyMMdd-HHmmss", CultureInfo.InvariantCulture);
        var baseName = $"{result.Backend.Name}-{stamp}";
        var jsonPath = Path.Combine(outputDir, $"{baseName}.json");
        var markdownPath = Path.Combine(outputDir, $"{baseName}.md");

        var jsonOptions = new JsonSerializerOptions
        {
            WriteIndented = true,
            PropertyNamingPolicy = JsonNamingPolicy.CamelCase
        };
        File.WriteAllText(jsonPath, JsonSerializer.Serialize(result, jsonOptions), Encoding.UTF8);

        File.WriteAllText(markdownPath, BuildMarkdown(result), Encoding.UTF8);
        return new BenchmarkOutput(result, jsonPath, markdownPath);
    }

    private static string BuildMarkdown(BenchmarkResult result)
    {
        var sb = new StringBuilder();

        sb.AppendLine("# Attention pipeline spike result — " + result.Backend.Name);
        sb.AppendLine();
        sb.AppendLine(FormattableString.Invariant(
            $"Thời điểm chạy: {result.Environment.StartedAtUtc.ToLocalTime():yyyy-MM-dd HH:mm:ss zzz}  "));
        sb.AppendLine(FormattableString.Invariant($"Thời lượng đo: {result.Stability.ActualDurationSeconds / 60:F1} phút  "));
        sb.AppendLine(FormattableString.Invariant($"Mẫu: {result.Stability.SuccessfulSamples}/{result.Stability.PlannedSamples} thành công  "));
        sb.AppendLine();

        sb.AppendLine("## Môi trường");
        sb.AppendLine();
        sb.AppendLine("| Hạng mục | Giá trị |");
        sb.AppendLine("|---|---|");
        sb.AppendLine($"| OS | {result.Environment.OsDescription} |");
        sb.AppendLine($"| Runtime | {result.Environment.Framework} ({result.Environment.ProcessArchitecture}) |");
        sb.AppendLine($"| CPU | {result.Environment.CpuName} ({result.Environment.LogicalProcessors} logical cores) |");
        sb.AppendLine(FormattableString.Invariant($"| RAM khả dụng cho GC | {result.Environment.ApproximatePhysicalMemoryBytes / 1024.0 / 1024.0 / 1024.0:F1} GiB |"));
        sb.AppendLine();

        sb.AppendLine("## Cấu hình");
        sb.AppendLine();
        sb.AppendLine("| Hạng mục | Giá trị |");
        sb.AppendLine("|---|---|");
        sb.AppendLine($"| Backend | `{result.Backend.Name}` |");
        sb.AppendLine($"| Model | {result.Backend.Model} |");
        sb.AppendLine(FormattableString.Invariant($"| Model load (ONNX Runtime / OpenCV report) | {result.Backend.ModelLoadMs} ms |"));
        sb.AppendLine($"| Capture API | {result.Initialization.CaptureApi} |");
        sb.AppendLine(FormattableString.Invariant($"| Độ phân giải yêu cầu | {result.Initialization.RequestedWidth}x{result.Initialization.RequestedHeight} |"));
        sb.AppendLine(FormattableString.Invariant($"| Score / NMS threshold | {result.Backend.ScoreThreshold:F2} / {result.Backend.NmsThreshold:F2} |"));
        sb.AppendLine(FormattableString.Invariant($"| Ngưỡng ánh sáng yếu | {result.Backend.LowLightThreshold:F0} |"));
        sb.AppendLine(FormattableString.Invariant($"| ONNX intra-op threads | {result.Backend.OnnxThreads} |"));
        sb.AppendLine();

        sb.AppendLine("## Khởi tạo");
        sb.AppendLine();
        sb.AppendLine("| Hạng mục | Giá trị |");
        sb.AppendLine("|---|---|");
        sb.AppendLine(FormattableString.Invariant($"| Mở camera | {result.Initialization.CameraOpenMs:F1} ms |"));
        sb.AppendLine(FormattableString.Invariant($"| Warm-up | {result.Initialization.WarmupMs:F1} ms |"));
        sb.AppendLine(FormattableString.Invariant($"| Inference đầu tiên | {result.Initialization.FirstInferenceMs:F1} ms |"));
        sb.AppendLine($"| Camera mở được | {(result.Initialization.CameraOpened ? "có" : "không")} |");
        if (!string.IsNullOrWhiteSpace(result.Initialization.CameraError))
            sb.AppendLine($"| Lỗi camera | {Escape(result.Initialization.CameraError)} |");
        sb.AppendLine();

        sb.AppendLine("## Tài nguyên (steady-state)");
        sb.AppendLine();
        sb.AppendLine("| Hạng mục | Trung bình | Cao nhất |");
        sb.AppendLine("|---|---|---|");
        sb.AppendLine(FormattableString.Invariant($"| CPU (% toàn máy) | {result.Resources.AvgCpuPercentNormalized:F2}% | {result.Resources.MaxCpuPercentNormalized:F2}% |"));
        sb.AppendLine(FormattableString.Invariant($"| CPU (% một core) | {result.Resources.AvgCpuPercentSingleCore:F2}% | {result.Resources.MaxCpuPercentSingleCore:F2}% |"));
        sb.AppendLine(FormattableString.Invariant($"| Working set | {result.Resources.AvgWorkingSetMb:F1} MiB | {result.Resources.MaxWorkingSetMb:F1} MiB |"));
        sb.AppendLine(FormattableString.Invariant($"| Private memory | {result.Resources.AvgPrivateMb:F1} MiB | {result.Resources.MaxPrivateMb:F1} MiB |"));
        sb.AppendLine();
        sb.AppendLine("CPU trung bình lấy mẫu mỗi 1 giây trong suốt thời gian đo (sau warm-up).");
        sb.AppendLine();

        sb.AppendLine("## Độ trễ pipeline");
        sb.AppendLine();
        sb.AppendLine("| Hạng mục | P50 | P95 | P99 | Max |");
        sb.AppendLine("|---|---|---|---|---|");
        sb.AppendLine(FormattableString.Invariant($"| Capture (ms) | {result.CaptureLatency.P50Ms:F1} | {result.CaptureLatency.P95Ms:F1} | {result.CaptureLatency.P99Ms:F1} | {result.CaptureLatency.MaxMs:F1} |"));
        sb.AppendLine(FormattableString.Invariant($"| Detect + inference (ms) | {result.DetectLatency.P50Ms:F1} | {result.DetectLatency.P95Ms:F1} | {result.DetectLatency.P99Ms:F1} | {result.DetectLatency.MaxMs:F1} |"));
        sb.AppendLine();

        sb.AppendLine("## Độ ổn định");
        sb.AppendLine();
        sb.AppendLine("| Hạng mục | Giá trị |");
        sb.AppendLine("|---|---|");
        sb.AppendLine($"| Mẫu dự kiến | {result.Stability.PlannedSamples} |");
        sb.AppendLine($"| Mẫu thành công | {result.Stability.SuccessfulSamples} |");
        sb.AppendLine($"| Mẫu lỗi | {result.Stability.FailedSamples} |");
        sb.AppendLine($"| Mẫu camera không khả dụng | {result.Stability.CameraUnavailableSamples} |");
        sb.AppendLine($"| Số lần thử mở lại camera | {result.Stability.ReopenAttempts} |");
        sb.AppendLine($"| Chuỗi lỗi liên tiếp dài nhất | {result.Stability.LongestFailureStreak} |");
        sb.AppendLine(FormattableString.Invariant($"| Thời lượng thực tế | {result.Stability.ActualDurationSeconds / 60:F2} phút |"));
        sb.AppendLine(FormattableString.Invariant($"| Tỷ lệ phát hiện mặt | {result.FaceDetectedPercent:F1}% |"));
        sb.AppendLine(FormattableString.Invariant($"| Độ sáng trung bình / thấp nhất | {result.LuminanceAvg:F1} / {result.LuminanceMin:F1} |"));
        sb.AppendLine();

        sb.AppendLine("## Phân bố trạng thái");
        sb.AppendLine();
        sb.AppendLine("### Trạng thái đã chốt (debounce)");
        sb.AppendLine();
        sb.AppendLine("| Trạng thái | Thời lượng | Tỷ lệ |");
        sb.AppendLine("|---|---|---|");
        foreach (var duration in result.CommittedDurations)
            sb.AppendLine(FormattableString.Invariant($"| {duration.Name} | {duration.Seconds:F1} s | {duration.PercentOfKnown:F1}% |"));
        sb.AppendLine();
        sb.AppendLine("### Trạng thái thô theo từng mẫu");
        sb.AppendLine();
        sb.AppendLine("| Trạng thái | Thời lượng | Tỷ lệ |");
        sb.AppendLine("|---|---|---|");
        foreach (var duration in result.RawDurations)
            sb.AppendLine(FormattableString.Invariant($"| {duration.Name} | {duration.Seconds:F1} s | {duration.PercentOfKnown:F1}% |"));
        sb.AppendLine();
        sb.AppendLine("Thời lượng được quy cho trạng thái của mẫu hiện tại trong khoảng giữa hai mẫu; khoảng thời gian trước mẫu đầu tiên không được tính.");
        sb.AppendLine();

        sb.AppendLine("## Ghi chú");
        sb.AppendLine();
        foreach (var note in result.Notes)
            sb.AppendLine($"- {note}");
        sb.AppendLine();

        sb.AppendLine("<details>");
        sb.AppendLine("<summary>Chi tiết từng mẫu</summary>");
        sb.AppendLine();
        sb.AppendLine("| # | Thời điểm (local) | Capture ms | Detect ms | Luminance | Mặt | Faces | Conf | Yaw | Pitch | Mắt | Raw | Committed | Lý do | Lỗi |");
        sb.AppendLine("|---|---|---|---|---|---|---|---|---|---|---|---|---|---|---|");
        foreach (var sample in result.Samples)
        {
            var localTime = sample.TimestampUtc.ToLocalTime();
            sb.AppendLine(FormattableString.Invariant(
                $"| {sample.Index} | {localTime:HH:mm:ss} | {sample.CaptureMs:F1} | {sample.DetectMs:F1} | {sample.Luminance:F1} | {(sample.FacePresent ? "có" : "không")} | {sample.FaceCount} | {sample.Confidence:F2} | {sample.YawRatio:F2} | {sample.PitchRatio:F2} | {sample.EyeCount} | {sample.RawState} | {sample.CommittedState} | {sample.Reason} | {Escape(sample.Error)} |"));
        }

        sb.AppendLine();
        sb.AppendLine("</details>");
        sb.AppendLine();
        sb.AppendLine("> Quyền riêng tư: báo cáo chỉ chứa số liệu tổng hợp và các trường số học (confidence, yaw/pitch, số mắt); không có khung hình, ảnh, tọa độ hộp, landmark hay embedding nào được ghi ra đĩa.");
        sb.AppendLine();

        return sb.ToString();
    }

    private static string Escape(string? value)
    {
        if (string.IsNullOrEmpty(value))
            return string.Empty;

        return value.Replace("|", "\\|").Replace("\r", " ").Replace("\n", " ");
    }
}
