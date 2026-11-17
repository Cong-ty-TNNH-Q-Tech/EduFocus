using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using System.Text.Json.Serialization;
using EduFocus.Cloud.Contracts.Common;
using EduFocus.Cloud.Contracts.Pairing;
using EduFocus.Cloud.Data;
using Microsoft.EntityFrameworkCore;

namespace EduFocus.Cloud.Tests;

[Collection("Postgres")]
public sealed class RateLimitTests
{
    private const string TestDbVariable = "EDUFOCUS_CLOUD_TEST_DB";

    private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web)
    {
        Converters = { new JsonStringEnumConverter(JsonNamingPolicy.CamelCase) },
    };

    [Fact]
    public async Task Pairing_create_is_rate_limited_per_client()
    {
        var connectionString = Environment.GetEnvironmentVariable(TestDbVariable);
        if (string.IsNullOrWhiteSpace(connectionString))
        {
            return;
        }

        await using (var db = new CloudDbContext(
            new DbContextOptionsBuilder<CloudDbContext>().UseNpgsql(connectionString).Options))
        {
            await db.Database.EnsureDeletedAsync();
            await db.Database.MigrateAsync();
        }

        await using var factory = new CloudApiFactory(connectionString, new Dictionary<string, string>
        {
            ["RateLimiting:PairingCreatePerHour"] = "2",
        });
        using var client = factory.CreateClient();

        for (var i = 0; i < 2; i++)
        {
            var allowed = await client.PostAsJsonAsync(
                "/v1/pairing/sessions",
                new CreatePairingSessionRequest($"PC {i}", "windows"));
            Assert.Equal(HttpStatusCode.OK, allowed.StatusCode);
        }

        var limited = await client.PostAsJsonAsync(
            "/v1/pairing/sessions",
            new CreatePairingSessionRequest("PC 3", "windows"));

        Assert.Equal(HttpStatusCode.TooManyRequests, limited.StatusCode);
        Assert.NotNull(limited.Headers.RetryAfter);
        var body = await limited.Content.ReadFromJsonAsync<ApiErrorResponse>(Json);
        Assert.Equal("rate_limited", body!.Error.Code);
    }
}
