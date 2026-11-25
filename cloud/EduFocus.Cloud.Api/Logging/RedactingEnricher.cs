using Serilog.Core;
using Serilog.Events;

namespace EduFocus.Cloud.Api.Logging;

public sealed class RedactingEnricher : ILogEventEnricher
{
    private static readonly HashSet<string> SensitiveNames = new(StringComparer.OrdinalIgnoreCase)
    {
        "auth",
        "authorization",
        "code",
        "desktopToken",
        "endpoint",
        "mobileToken",
        "p256dh",
        "pairingCode",
        "password",
        "secret",
        "token",
        "vapidPrivateKey",
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
