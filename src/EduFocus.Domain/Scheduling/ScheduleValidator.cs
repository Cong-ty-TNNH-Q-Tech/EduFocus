using EduFocus.Domain.Entities;

namespace EduFocus.Domain.Scheduling;

public static class ScheduleValidator
{
    public static void EnsureNoOverlaps(IEnumerable<ScheduleWindow> windows)
    {
        ArgumentNullException.ThrowIfNull(windows);

        var enabled = windows.Where(window => window.IsEnabled).ToList();

        for (var i = 0; i < enabled.Count; i++)
        {
            for (var j = i + 1; j < enabled.Count; j++)
            {
                if (enabled[i].Overlaps(enabled[j]))
                {
                    throw new ArgumentException(
                        "Enabled schedule windows must not overlap.",
                        nameof(windows));
                }
            }
        }
    }

    public static void EnsureCanAdd(IEnumerable<ScheduleWindow> existing, ScheduleWindow candidate)
    {
        ArgumentNullException.ThrowIfNull(existing);
        ArgumentNullException.ThrowIfNull(candidate);

        if (!candidate.IsEnabled)
            return;

        foreach (var window in existing)
        {
            if (window.IsEnabled && window.Overlaps(candidate))
            {
                throw new ArgumentException(
                    "Window overlaps an enabled schedule window.",
                    nameof(candidate));
            }
        }
    }
}
