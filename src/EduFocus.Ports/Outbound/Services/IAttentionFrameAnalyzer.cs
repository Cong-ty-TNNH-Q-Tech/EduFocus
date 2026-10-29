using EduFocus.Ports.DTOs;

namespace EduFocus.Ports.Outbound.Services;

public interface IAttentionFrameAnalyzer : IDisposable
{
    string BackendName { get; }
    string ModelInfo { get; }
    long ModelLoadMs { get; }
    bool IsOpen { get; }
    AttentionOpenResult Open();
    AttentionFrameResult AnalyzeFrame();
}
