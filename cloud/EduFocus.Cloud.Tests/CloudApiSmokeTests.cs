using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using System.Text.Json.Serialization;
using EduFocus.Cloud.Contracts.Common;
using EduFocus.Cloud.Contracts.Pairing;

namespace EduFocus.Cloud.Tests;

public sealed class CloudApiSmokeTests
{
    private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web)
    {
        Converters = { new JsonStringEnumConverter(JsonNamingPolicy.CamelCase) },
    };

    [Fact]
    public async Task Api_boots_and_returns_coded_json_errors_without_database()
    {
        await using var factory = new CloudApiFactory(
            "Host=localhost;Port=5999;Database=missing;Username=x;Password=x;Timeout=1");
        using var client = factory.CreateClient();

        var root = await client.GetAsync("/v1/");
        Assert.Equal(HttpStatusCode.OK, root.StatusCode);

        var unauthorized = await client.GetAsync("/v1/link/status");
        Assert.Equal(HttpStatusCode.Unauthorized, unauthorized.StatusCode);
        var unauthorizedBody = await unauthorized.Content.ReadFromJsonAsync<ApiErrorResponse>(Json);
        Assert.Equal("unauthorized", unauthorizedBody!.Error.Code);

        var invalidClaim = await client.PostAsJsonAsync(
            "/v1/pairing/claim",
            new ClaimPairingRequest("!!!", "Parent phone", "android"));
        Assert.Equal(HttpStatusCode.Gone, invalidClaim.StatusCode);
        var invalidClaimBody = await invalidClaim.Content.ReadFromJsonAsync<ApiErrorResponse>(Json);
        Assert.Equal("pairing_unavailable", invalidClaimBody!.Error.Code);
    }
}
