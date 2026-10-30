using EduFocus.Domain.Attention;
using EduFocus.Ports.DTOs;

namespace EduFocus.UseCases.Attention;

/// <summary>
/// Translates capture-layer failures into camera health problems so the live
/// status and the camera check describe the same situation with the same words.
/// </summary>
public static class CameraProblemMapper
{
    public static CameraHealthProblem FromFailure(CameraFailureKind failure) =>
        failure switch
        {
            CameraFailureKind.Busy => CameraHealthProblem.Busy,
            CameraFailureKind.PermissionDenied => CameraHealthProblem.PermissionDenied,
            CameraFailureKind.NotFound => CameraHealthProblem.NoCamera,
            CameraFailureKind.Disconnected => CameraHealthProblem.Disconnected,
            CameraFailureKind.Timeout => CameraHealthProblem.Timeout,
            CameraFailureKind.None => CameraHealthProblem.None,
            _ => CameraHealthProblem.Unknown
        };
}
