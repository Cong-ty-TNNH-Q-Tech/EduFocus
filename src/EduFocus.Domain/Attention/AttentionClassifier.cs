namespace EduFocus.Domain.Attention;

public sealed record AttentionClassification(RawAttentionState State, float Confidence, string Reason);

public static class AttentionClassifier
{
    public static AttentionClassification Classify(
        FaceObservation observation,
        double luminance,
        AttentionThresholds thresholds)
    {
        if (!observation.CameraAvailable)
            return new AttentionClassification(RawAttentionState.CameraUnavailable, 0, "camera-unavailable");

        if (!observation.FacePresent)
        {
            if (luminance < thresholds.LowLightLuminance)
                return new AttentionClassification(RawAttentionState.LowLight, 0.1f, "no-face-low-light");

            return new AttentionClassification(RawAttentionState.Absent, 0.5f, "no-face");
        }

        // Eye closure is only available when the model supplies an EAR-like
        // signal; a null value means "unknown, do not guess".
        if (observation.EyesClosed == true)
            return new AttentionClassification(RawAttentionState.Drowsy, observation.Confidence, "eyes-closed");

        var frontal = observation.LandmarksAvailable
            ? observation.HeadYawRatio <= thresholds.YawRatio && observation.HeadPitchRatio <= thresholds.PitchRatio
            : observation.HeadYawRatio <= thresholds.NoLandmarkYawRatio &&
              observation.HeadPitchRatio <= thresholds.NoLandmarkPitchRatio;

        return frontal
            ? new AttentionClassification(RawAttentionState.Focused, observation.Confidence, "frontal")
            : new AttentionClassification(RawAttentionState.Distracted, observation.Confidence, "off-screen");
    }
}
