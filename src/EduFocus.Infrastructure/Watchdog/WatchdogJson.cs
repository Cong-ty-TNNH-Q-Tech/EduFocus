using System.Text.Json;

namespace EduFocus.Infrastructure.Watchdog;

internal static class WatchdogJson
{
    public static readonly JsonSerializerOptions Options =
        new(JsonSerializerDefaults.Web) { WriteIndented = true };
}
