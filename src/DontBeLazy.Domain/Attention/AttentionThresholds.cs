namespace DontBeLazy.Domain.Attention;

public sealed record AttentionThresholds(
    double YawRatio = 0.30,
    double PitchRatio = 0.35,
    double NoLandmarkYawRatio = 0.45,
    double NoLandmarkPitchRatio = 0.45,
    double DistractedAfterSeconds = 20,
    double AbsentAfterSeconds = 30,
    double LowLightAfterSeconds = 30,
    double FocusedAfterSeconds = 0,
    double LowLightLuminance = 40);
