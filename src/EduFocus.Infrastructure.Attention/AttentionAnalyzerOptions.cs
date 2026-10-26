namespace EduFocus.Infrastructure.Attention;

public sealed record AttentionAnalyzerOptions(
    int CameraIndex = 0,
    string CaptureApi = "msmf",
    int Width = 640,
    int Height = 480,
    int FlushFrames = 1,
    int OnnxThreads = 1,
    float ScoreThreshold = 0.6f,
    float NmsThreshold = 0.3f);
