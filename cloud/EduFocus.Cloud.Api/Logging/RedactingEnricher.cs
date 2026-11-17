using Serilog.Core;
using Serilog.Events;

namespace EduFocus.Cloud.Api.Logging;

public sealed class RedactingEnricher : ILogEventEnricher
{
    private static readonly HashSet<string> SensitiveNames = new(StringComparer.OrdinalIgnoreCase)
    {
        "authorization",
        "code",
        "desktopToken",
        "fcmToken",
        "mobileToken",
        "pairingCode",
        "password",
        "pushToken",
        "secret",
        "token",
    };

    public void Enrich(LogEvent logEvent, ILogEventPropertyFactory propertyFactory)
    {
        foreach (var property in logEvent.Properties.ToList())
        {
            if (SensitiveNames.Contains(property.Key))
            {
                logEvent.AddOrUpdateProperty(
                    propertyFactory.CreateProperty(property.Key, "[REDACTED]"));
            }
        }
    }
}
