using EduFocus.Domain.Attention;
using EduFocus.Ports.DTOs;
using EduFocus.Ports.Outbound.Services;

namespace EduFocus.UseCases.Tests.TestSupport;

internal sealed class FakeAttentionFrameAnalyzer : IAttentionFrameAnalyzer
{
    public string BackendName { get; set; } = "fake";

    public string ModelInfo { get; set; } = "fake-model";

    public long ModelLoadMs { get; set; }

    public int AnalyzeCalls { get; private set; }

    public int DisposeCalls { get; private set; }

    public List<CameraFrame> Frames { get; } = [];

    public List<bool> FrameDisposedAtAnalyze { get; } = [];

    public Queue<AttentionFrameResult> Results { get; } = new();

    public Func<CameraFrame, AttentionFrameResult>? Handler { get; set; }

    public AttentionFrameResult DefaultResult { get; set; } = new(Face(), 120, 7, Error: null);

    public AttentionFrameResult Analyze(CameraFrame frame)
    {
        AnalyzeCalls++;
        Frames.Add(frame);
        FrameDisposedAtAnalyze.Add(frame.IsDisposed);

        if (Handler is not null)
            return Handler(frame);

        return Results.TryDequeue(out var result) ? result : DefaultResult;
    }

    public void Dispose() => DisposeCalls++;

    public static FaceObservation Face() =>
        FaceObservation.NoFace with
        {
            FacePresent = true,
            FaceCount = 1,
            Confidence = 0.9f,
            X = 10,
            Y = 10,
            Width = 50,
            Height = 50,
            EyeCount = 2
        };
}
