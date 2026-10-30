using EduFocus.Ports.DTOs;

namespace EduFocus.Ports.Outbound.Services;

/// <summary>
/// Runs face inference on one captured frame. Implementations never capture,
/// persist or dispose the frame; the caller keeps ownership and releases it
/// immediately after the call (UC08, issue #31).
/// </summary>
public interface IAttentionFrameAnalyzer : IDisposable
{
    string BackendName { get; }

    string ModelInfo { get; }

    long ModelLoadMs { get; }

    AttentionFrameResult Analyze(CameraFrame frame);
}
