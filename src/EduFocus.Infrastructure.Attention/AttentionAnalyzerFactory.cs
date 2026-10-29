using EduFocus.Infrastructure.Attention.Detection;
using EduFocus.Ports.Outbound.Services;

namespace EduFocus.Infrastructure.Attention;

public static class AttentionAnalyzerFactory
{
    public static IAttentionFrameAnalyzer Create(
        string backend,
        AttentionAnalyzerOptions options,
        string modelDirectory)
    {
        var detector = CreateDetector(backend, options, modelDirectory);
        return new OpenCvAttentionFrameAnalyzer(detector, options);
    }

    public static IFaceDetector CreateDetector(
        string backend,
        AttentionAnalyzerOptions options,
        string modelDirectory)
    {
        return backend.Trim().ToLowerInvariant() switch
        {
            "yunet" => CreateYuNet(options, modelDirectory),
            "haar" => CreateHaar(modelDirectory),
            _ => throw new ArgumentException($"Unknown backend: {backend}. Supported: yunet, haar.", nameof(backend))
        };
    }

    private static YuNetOnnxDetector CreateYuNet(AttentionAnalyzerOptions options, string modelDirectory)
    {
        var modelPath = Path.Combine(modelDirectory, "face_detection_yunet_2023mar.onnx");
        if (!File.Exists(modelPath))
            throw new FileNotFoundException("YuNet model not found.", modelPath);

        return new YuNetOnnxDetector(
            modelPath,
            options.OnnxThreads,
            options.ScoreThreshold,
            options.NmsThreshold);
    }

    private static HaarCascadeDetector CreateHaar(string modelDirectory)
    {
        var facePath = Path.Combine(modelDirectory, "haarcascade_frontalface_default.xml");
        var eyePath = Path.Combine(modelDirectory, "haarcascade_eye.xml");

        if (!File.Exists(facePath))
            throw new FileNotFoundException("Haar face cascade not found.", facePath);
        if (!File.Exists(eyePath))
            throw new FileNotFoundException("Haar eye cascade not found.", eyePath);

        return new HaarCascadeDetector(facePath, eyePath);
    }
}
