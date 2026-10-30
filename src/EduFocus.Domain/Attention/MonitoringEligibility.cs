namespace EduFocus.Domain.Attention;

/// <summary>
/// Decides whether attention monitoring is expected for the current context
/// (UC08, BR-15). Monitoring must be enabled and, when
/// <c>MonitorOnlyDuringClasses</c> is set, only online-class foreground is
/// eligible, so off-class time never enters the coverage denominator.
/// </summary>
public static class MonitoringEligibility
{
    public static bool IsEligible(
        bool monitoringEnabled,
        bool monitorOnlyDuringClasses,
        bool isOnlineClass) =>
        monitoringEnabled && (!monitorOnlyDuringClasses || isOnlineClass);
}
