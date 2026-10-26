namespace EduFocus.Domain.Attention;

public enum AttentionState
{
    Focused,
    Distracted,
    Absent,
    Drowsy,
    CameraUnavailable
}

public enum RawAttentionState
{
    Unknown,
    Focused,
    Distracted,
    Absent,
    LowLight,
    CameraUnavailable
}
