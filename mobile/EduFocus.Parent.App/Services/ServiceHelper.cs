using Microsoft.Extensions.DependencyInjection;

namespace EduFocus.Parent.App.Services;

public static class ServiceHelper
{
    public static T GetService<T>() where T : notnull
    {
        var services = IPlatformApplication.Current?.Services
            ?? throw new InvalidOperationException("MAUI services are not available.");
        return services.GetRequiredService<T>();
    }
}
