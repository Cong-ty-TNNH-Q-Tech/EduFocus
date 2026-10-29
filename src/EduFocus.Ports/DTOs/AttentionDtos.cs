using EduFocus.Domain.Attention;

namespace EduFocus.Ports.DTOs;

public sealed record AttentionOpenResult(
    bool Success,
    double OpenMs,
    string CaptureApi,
    string? Error);

public sealed record AttentionFrameResult(
    bool CameraAvailable,
    bool Captured,
    FaceObservation? Face,
    double Luminance,
    double CaptureMs,
    double DetectMs,
    string? Error);
