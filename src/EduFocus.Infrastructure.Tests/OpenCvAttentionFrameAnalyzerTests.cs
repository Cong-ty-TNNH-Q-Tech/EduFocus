using EduFocus.Domain.Attention;
using EduFocus.Infrastructure.Attention;
using EduFocus.Infrastructure.Attention.Detection;
using EduFocus.Ports.DTOs;
using FluentAssertions;
using OpenCvSharp;

namespace EduFocus.Infrastructure.Tests;

public sealed class OpenCvAttentionFrameAnalyzerTests
{
    [Fact]
    public void The_face_median_judges_the_face_region_not_the_bright_room()
    {
        using var frame = Frame(background: 200, region: 20, regionRect: new Rect(20, 20, 60, 60));
        using var analyzer = new OpenCvAttentionFrameAnalyzer(new FixedDetector(Box(30, 30, 40, 40)));

        var result = analyzer.Analyze(frame);

        result.Error.Should().BeNull();
        result.Luminance.Should().BeGreaterThan(150);
        result.Face.FacePresent.Should().BeTrue();
        result.FaceLuminance.Should().BeApproximately(20, 1);
    }

    [Fact]
    public void A_lit_face_on_a_dark_background_keeps_a_bright_face_median()
    {
        using var frame = Frame(background: 10, region: 220, regionRect: new Rect(20, 20, 60, 60));
        using var analyzer = new OpenCvAttentionFrameAnalyzer(new FixedDetector(Box(30, 30, 40, 40)));

        var result = analyzer.Analyze(frame);

        result.Luminance.Should().BeLessThan(40);
        result.FaceLuminance.Should().BeApproximately(220, 1);
    }

    [Fact]
    public void A_frame_without_a_face_has_no_face_luminance()
    {
        using var frame = Frame(background: 200, region: 20, regionRect: new Rect(20, 20, 60, 60));
        using var analyzer = new OpenCvAttentionFrameAnalyzer(new FixedDetector(FaceObservation.NoFace));

        var result = analyzer.Analyze(frame);

        result.Face.FacePresent.Should().BeFalse();
        result.FaceLuminance.Should().BeNull();
        result.Luminance.Should().BeApproximately(183.8, 0.5);
    }

    [Fact]
    public void A_face_box_outside_the_frame_has_no_face_luminance()
    {
        using var frame = Frame(background: 200, region: 20, regionRect: new Rect(20, 20, 60, 60));
        using var analyzer = new OpenCvAttentionFrameAnalyzer(new FixedDetector(Box(300, 300, 50, 50)));

        var result = analyzer.Analyze(frame);

        result.Face.FacePresent.Should().BeTrue();
        result.FaceLuminance.Should().BeNull();
    }

    private static FaceObservation Box(float x, float y, float width, float height) =>
        FaceObservation.NoFace with
        {
            FacePresent = true,
            FaceCount = 1,
            Confidence = 0.9f,
            X = x,
            Y = y,
            Width = width,
            Height = height,
            EyeCount = 2
        };

    private static CameraFrame Frame(byte background, byte region, Rect regionRect)
    {
        const int width = 200;
        const int height = 200;
        var pixels = new byte[width * height * 3];

        for (var y = 0; y < height; y++)
        {
            for (var x = 0; x < width; x++)
            {
                var inRegion = x >= regionRect.X && x < regionRect.Right
                    && y >= regionRect.Y && y < regionRect.Bottom;
                var value = inRegion ? region : background;
                var offset = (y * width + x) * 3;
                pixels[offset] = value;
                pixels[offset + 1] = value;
                pixels[offset + 2] = value;
            }
        }

        return new CameraFrame(width, height, pixels);
    }

    private sealed class FixedDetector(FaceObservation observation) : IFaceDetector
    {
        public string Name => "fixed";

        public string Model => "synthetic";

        public long LoadMs => 0;

        public FaceObservation Detect(Mat bgrFrame) => observation;

        public void Dispose()
        {
        }
    }
}
