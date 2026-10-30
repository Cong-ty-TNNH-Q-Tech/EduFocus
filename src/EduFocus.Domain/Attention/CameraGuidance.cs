namespace EduFocus.Domain.Attention;

/// <summary>
/// Parent-facing remediation text for camera problems (UC08, issue #34). The
/// same strings feed the live status indicator and the calibration result so the
/// advice never diverges between screens.
/// </summary>
public static class CameraGuidance
{
    public const string SubstitutedDeviceNote =
        "Camera đã lưu không còn kết nối; EduFocus đang dùng camera mặc định. Hãy chọn lại camera trong Parent Mode.";

    public static string Summary(CameraHealthResult result)
    {
        ArgumentNullException.ThrowIfNull(result);

        return result.Health switch
        {
            CameraHealth.Ready => "Camera hoạt động tốt.",
            CameraHealth.TooDark => "Camera hoạt động nhưng hình ảnh quá tối.",
            CameraHealth.NoFace => "Camera hoạt động nhưng không thấy khuôn mặt.",
            CameraHealth.NoImage => "Camera không trả về hình ảnh.",
            _ => "Camera không khả dụng."
        };
    }

    public static string? Describe(
        CameraHealthResult result,
        bool supportsMultiAppCamera,
        bool deviceSubstituted = false)
    {
        ArgumentNullException.ThrowIfNull(result);

        var guidance = result.Health switch
        {
            CameraHealth.Ready => null,
            CameraHealth.TooDark =>
                "Hãy bật thêm đèn hoặc di chuyển đến nơi sáng hơn rồi kiểm tra lại.",
            CameraHealth.NoFace =>
                "Hãy ngồi đối diện camera ở khoảng cách phù hợp rồi kiểm tra lại.",
            CameraHealth.NoImage =>
                "Hãy đóng ứng dụng đang dùng camera hoặc kiểm tra lại thiết bị.",
            _ => DescribeProblem(result.Problem, supportsMultiAppCamera)
        };

        return AppendSubstitutedDeviceNote(guidance, deviceSubstituted);
    }

    public static string? Describe(
        CameraHealthProblem problem,
        bool supportsMultiAppCamera,
        bool deviceSubstituted = false)
    {
        var guidance = DescribeProblem(problem, supportsMultiAppCamera);
        return AppendSubstitutedDeviceNote(guidance, deviceSubstituted);
    }

    private static string? DescribeProblem(CameraHealthProblem problem, bool supportsMultiAppCamera) =>
        problem switch
        {
            CameraHealthProblem.Busy when supportsMultiAppCamera =>
                "Camera đang được ứng dụng khác sử dụng. Nếu đang học bằng Meet/Teams/Zoom, hãy bật Multi-App Camera trong Settings → Bluetooth & devices → Cameras để EduFocus có thể quan sát song song.",
            CameraHealthProblem.Busy =>
                "Camera đang được ứng dụng khác sử dụng. Hãy đóng ứng dụng đang dùng camera (Meet/Teams/Zoom) hoặc dùng một thiết bị khác cho lớp học.",
            CameraHealthProblem.PermissionDenied =>
                "Windows đang chặn quyền camera. Mở Settings → Privacy & security → Camera và bật quyền cho EduFocus.",
            CameraHealthProblem.NoCamera =>
                "Không tìm thấy camera. Hãy kết nối webcam hoặc bật camera tích hợp rồi kiểm tra lại.",
            CameraHealthProblem.Disconnected =>
                "Camera đã bị ngắt kết nối. Hãy cắm lại camera rồi kiểm tra lại.",
            CameraHealthProblem.Timeout =>
                "Camera không phản hồi. Hãy đóng ứng dụng đang dùng camera và kiểm tra lại.",
            CameraHealthProblem.MonitoringActive =>
                "Dừng bảo vệ hoặc phiên giám sát trước khi kiểm tra camera để tránh tranh chấp thiết bị.",
            CameraHealthProblem.Unknown =>
                "Không thể mở camera. Hãy đóng ứng dụng đang dùng camera rồi kiểm tra lại.",
            _ => null
        };

    private static string? AppendSubstitutedDeviceNote(string? guidance, bool deviceSubstituted)
    {
        if (!deviceSubstituted)
            return guidance;

        return guidance is null ? SubstitutedDeviceNote : $"{guidance} {SubstitutedDeviceNote}";
    }
}
