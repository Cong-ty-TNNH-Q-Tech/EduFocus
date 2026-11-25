using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using System.Text.Json.Serialization;
using EduFocus.Cloud.Api.Auth;
using EduFocus.Cloud.Api.RateLimiting;
using EduFocus.Cloud.Contracts.Common;
using EduFocus.Cloud.Contracts.Pairing;
using EduFocus.Cloud.Data;
using EduFocus.Cloud.Data.Entities;
using Microsoft.AspNetCore.Http;
using Microsoft.EntityFrameworkCore;

namespace EduFocus.Cloud.Tests;

[Collection("CloudDatabase")]
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
            new DbContextOptionsBuilder<CloudDbContext>().UseSqlite(connectionString).Options))
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

    [Fact]
    public void Token_key_normalizes_scheme_casing_and_whitespace()
    {
        var (token, _) = DeviceTokenService.Create(DeviceKind.Desktop);

        var canonical = RateLimitSetup.TokenKey(ContextWithAuth($"Bearer {token}"));
        var lowercase = RateLimitSetup.TokenKey(ContextWithAuth($"bearer {token}"));
        var uppercase = RateLimitSetup.TokenKey(ContextWithAuth($"BEARER {token}"));
        var extraSpaces = RateLimitSetup.TokenKey(ContextWithAuth($"Bearer   {token}  "));

        Assert.StartsWith("token:", canonical);
        Assert.Equal(canonical, lowercase);
        Assert.Equal(canonical, uppercase);
        Assert.Equal(canonical, extraSpaces);
    }

    [Theory]
    [InlineData("")]
    [InlineData("x-random-header")]
    [InlineData("Bearer not-a-valid-device-token")]
    [InlineData("Basic dXNlcjpwYXNz")]
    public void Garbage_or_invalid_authorization_headers_share_client_ip_partition(string header)
    {
        var key = RateLimitSetup.TokenKey(ContextWithAuth(header, IPAddress.Parse("203.0.113.10")));
        Assert.Equal("anonymous:203.0.113.10", key);
    }

    private static DefaultHttpContext ContextWithAuth(string header, IPAddress? ip = null)
    {
        var context = new DefaultHttpContext();
        if (!string.IsNullOrEmpty(header))
        {
            context.Request.Headers.Authorization = header;
        }
        context.Connection.RemoteIpAddress = ip ?? IPAddress.Loopback;
        return context;
    }
}
