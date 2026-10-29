using EduFocus.Domain.Attention;
using FluentAssertions;

namespace EduFocus.Domain.Tests.Attention;

public class CameraGuidanceTests
{
    [Fact]
    public void Busy_camera_on_windows_11_guides_multi_app_camera()
    {
        var guidance = CameraGuidance.Describe(CameraHealthProblem.Busy, supportsMultiAppCamera: true);

        guidance.Should().Contain("Multi-App Camera");
        guidance.Should().Contain("Settings → Bluetooth & devices → Cameras");
        guidance.Should().NotContain("dùng một thiết bị khác");
    }

    [Fact]
    public void Busy_camera_without_multi_app_support_guides_closing_the_other_app()
    {
        var guidance = CameraGuidance.Describe(CameraHealthProblem.Busy, supportsMultiAppCamera: false);

        guidance.Should().NotBeNull();
        guidance.Should().Contain("đóng ứng dụng đang dùng camera");
        guidance.Should().NotContain("Multi-App Camera");
    }

    [Fact]
    public void Permission_guidance_points_to_the_windows_privacy_settings()
    {
        var guidance = CameraGuidance.Describe(CameraHealthProblem.PermissionDenied, supportsMultiAppCamera: false);

        guidance.Should().Contain("Settings → Privacy & security → Camera");
    }

    [Theory]
    [InlineData(CameraHealthProblem.NoCamera)]
    [InlineData(CameraHealthProblem.Disconnected)]
    [InlineData(CameraHealthProblem.Timeout)]
    [InlineData(CameraHealthProblem.Unknown)]
    [InlineData(CameraHealthProblem.MonitoringActive)]
    public void Every_unavailable_problem_has_remediation(CameraHealthProblem problem)
    {
        CameraGuidance.Describe(problem, supportsMultiAppCamera: false).Should().NotBeNullOrWhiteSpace();
    }

    [Fact]
    public void Ready_camera_has_no_remediation_unless_the_stored_device_disappeared()
    {
        var ready = Report(CameraHealth.Ready, CameraHealthProblem.None);

        CameraGuidance.Describe(ready, supportsMultiAppCamera: true).Should().BeNull();

        CameraGuidance.Describe(ready, supportsMultiAppCamera: true, deviceSubstituted: true)
            .Should().Be(CameraGuidance.SubstitutedDeviceNote);
    }

    [Fact]
    public void A_substituted_device_note_is_appended_to_the_problem_guidance()
    {
        var guidance = CameraGuidance.Describe(
            CameraHealthProblem.PermissionDenied,
            supportsMultiAppCamera: false,
            deviceSubstituted: true);

        guidance.Should().Contain("Settings → Privacy & security → Camera");
        guidance.Should().Contain(CameraGuidance.SubstitutedDeviceNote);
    }

    [Fact]
    public void Calibration_guidance_explains_dark_and_empty_frames()
    {
        CameraGuidance.Describe(Report(CameraHealth.TooDark, CameraHealthProblem.None), false)
            .Should().Contain("bật thêm đèn");
        CameraGuidance.Describe(Report(CameraHealth.NoFace, CameraHealthProblem.None), false)
            .Should().Contain("ngồi đối diện camera");
        CameraGuidance.Describe(Report(CameraHealth.NoImage, CameraHealthProblem.None), false)
            .Should().Contain("đóng ứng dụng đang dùng camera");
    }

    [Fact]
    public void Summary_matches_each_health_state()
    {
        CameraGuidance.Summary(Report(CameraHealth.Ready, CameraHealthProblem.None))
            .Should().Be("Camera hoạt động tốt.");
        CameraGuidance.Summary(Report(CameraHealth.TooDark, CameraHealthProblem.None))
            .Should().Contain("quá tối");
        CameraGuidance.Summary(Report(CameraHealth.NoFace, CameraHealthProblem.None))
            .Should().Contain("không thấy khuôn mặt");
        CameraGuidance.Summary(Report(CameraHealth.NoImage, CameraHealthProblem.None))
            .Should().Contain("không trả về hình ảnh");
        CameraGuidance.Summary(Report(CameraHealth.Unavailable, CameraHealthProblem.Busy))
            .Should().Be("Camera không khả dụng.");
    }

    [Fact]
    public void Null_results_are_rejected()
    {
        var act = () => CameraGuidance.Summary(null!);
        var describe = () => CameraGuidance.Describe((CameraHealthResult)null!, false);

        act.Should().Throw<ArgumentNullException>();
        describe.Should().Throw<ArgumentNullException>();
    }

    private static CameraHealthResult Report(CameraHealth health, CameraHealthProblem problem) =>
        new(health, problem, AttemptedFrames: 5, AnalyzedFrames: 4, FaceFrames: 3, MeanLuminance: 90);
}
