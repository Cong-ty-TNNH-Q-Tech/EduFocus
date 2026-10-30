using EduFocus.Domain.Attention;

namespace EduFocus.Ports.DTOs;

/// <summary>
/// Inference output for one captured frame. <see cref="Face"/> is never null;
/// check <see cref="Error"/> before using it. Frame bytes stay with the caller.
/// </summary>
public sealed record AttentionFrameResult(
    FaceObservation Face,
    double Luminance,
    double DetectMs,
    string? Error)
{
    public bool Succeeded => Error is null;
}

/// <summary>
/// One sampling tick. <see cref="Observation"/> is null when the camera was
/// unavailable or inference failed; samples never carry image data because the
/// frame is disposed before the sample is published.
/// </summary>
public sealed record AttentionSample(
    DateTimeOffset SampledAtUtc,
    CameraFailureKind CameraFailure,
    FaceObservation? Observation,
    double Luminance,
    double CaptureMs,
    double DetectMs,
    string? Error)
{
    public bool HasObservation => Observation is not null;
}
