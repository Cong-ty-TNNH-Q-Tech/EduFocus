using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;

namespace EduFocus.Cloud.Tests;

internal sealed class CloudApiFactory(
    string connectionString,
    IReadOnlyDictionary<string, string>? settings = null)
    : WebApplicationFactory<Program>
{
    protected override void ConfigureWebHost(IWebHostBuilder builder)
    {
        builder.UseEnvironment("Testing");
        builder.UseSetting("ConnectionStrings:Postgres", connectionString);
        builder.UseSetting("Cloud:PublicBaseUrl", "https://cloud.test");

        if (settings is null)
        {
            return;
        }

        foreach (var (key, value) in settings)
        {
            builder.UseSetting(key, value);
        }
    }
}
