namespace EduFocus.Domain.Attention;

public readonly record struct Landmark(float X, float Y);

public sealed record FaceObservation(
    bool CameraAvailable,
    bool FacePresent,
    int FaceCount,
    float Confidence,
    float X,
    float Y,
    float Width,
    float Height,
    Landmark? RightEye,
    Landmark? LeftEye,
    Landmark? Nose,
    Landmark? RightMouth,
    Landmark? LeftMouth,
    int EyeCount,
    double HeadYawRatio,
    double HeadPitchRatio,
    bool LandmarksAvailable,
    bool? EyesClosed = null)
{
    public static FaceObservation CameraUnavailable { get; } = new(
        CameraAvailable: false,
        FacePresent: false,
        FaceCount: 0,
        Confidence: 0,
        X: 0,
        Y: 0,
        Width: 0,
        Height: 0,
        RightEye: null,
        LeftEye: null,
        Nose: null,
        RightMouth: null,
        LeftMouth: null,
        EyeCount: 0,
        HeadYawRatio: 0,
        HeadPitchRatio: 0,
        LandmarksAvailable: false);

    public static FaceObservation NoFace { get; } = CameraUnavailable with { CameraAvailable = true };
}
