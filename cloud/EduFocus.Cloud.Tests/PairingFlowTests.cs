using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using System.Text.Json.Serialization;
using EduFocus.Cloud.Contracts.Common;
using EduFocus.Cloud.Contracts.Devices;
using EduFocus.Cloud.Contracts.Pairing;
using EduFocus.Cloud.Data;
using Microsoft.EntityFrameworkCore;

namespace EduFocus.Cloud.Tests;

[Collection("Postgres")]
public sealed class PairingFlowTests
{
    private const string TestDbVariable = "EDUFOCUS_CLOUD_TEST_DB";

    private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web)
    {
        Converters = { new JsonStringEnumConverter(JsonNamingPolicy.CamelCase) },
    };

    [Fact]
    public async Task Pair_claim_link_rotate_and_revoke()
    {
        var connectionString = Environment.GetEnvironmentVariable(TestDbVariable);
        if (string.IsNullOrWhiteSpace(connectionString))
        {
            return;
        }

        await ResetDatabaseAsync(connectionString);
        await using var factory = new CloudApiFactory(connectionString);
        using var anonymous = factory.CreateClient();

        var invalidTimeZone = await anonymous.PostAsJsonAsync(
            "/v1/pairing/sessions",
            new CreatePairingSessionRequest("Student PC", "windows", TimeZoneId: "Mars/Olympus"));
        Assert.Equal(HttpStatusCode.BadRequest, invalidTimeZone.StatusCode);
        Assert.Equal(
            "invalid_time_zone",
            (await invalidTimeZone.Content.ReadFromJsonAsync<ApiErrorResponse>(Json))!.Error.Code);

        var createResponse = await anonymous.PostAsJsonAsync(
            "/v1/pairing/sessions",
            new CreatePairingSessionRequest("Student PC", "windows", "2.1.0", "Asia/Ho_Chi_Minh"));
        Assert.Equal(HttpStatusCode.OK, createResponse.StatusCode);
        var session = await createResponse.Content.ReadFromJsonAsync<PairingSessionResponse>(Json);
        Assert.NotNull(session);
        Assert.Equal(8, session.Code.Length);
        Assert.StartsWith("efd_", session.DesktopToken);
        Assert.Equal("https://cloud.test", session.CloudBaseUrl);
        Assert.Contains($"c={session.Code}", session.PairingUri);
        Assert.True(session.ExpiresAtUtc > DateTimeOffset.UtcNow);

        using var desktop = CreateAuthenticatedClient(factory, session.DesktopToken);

        var unauthorized = await anonymous.GetAsync($"/v1/pairing/sessions/{session.SessionId}");
        Assert.Equal(HttpStatusCode.Unauthorized, unauthorized.StatusCode);
        Assert.Equal(
            "unauthorized",
            (await unauthorized.Content.ReadFromJsonAsync<ApiErrorResponse>(Json))!.Error.Code);

        var pendingResponse = await desktop.GetAsync($"/v1/pairing/sessions/{session.SessionId}");
        Assert.Equal(HttpStatusCode.OK, pendingResponse.StatusCode);
        var pendingJson = await pendingResponse.Content.ReadAsStringAsync();
        Assert.Contains("\"status\":\"pending\"", pendingJson);

        var malformedClaim = await anonymous.PostAsJsonAsync(
            "/v1/pairing/claim",
            new ClaimPairingRequest("!!!", "Parent phone", "android"));
        Assert.Equal(HttpStatusCode.Gone, malformedClaim.StatusCode);

        var claimCode = session.Code.ToLowerInvariant();
        var claimCodeWithSeparator = claimCode[..4] + "-" + claimCode[4..];
        var claimResponse = await anonymous.PostAsJsonAsync(
            "/v1/pairing/claim",
            new ClaimPairingRequest(
                claimCodeWithSeparator,
                "Parent phone",
                "android",
                "0.1.0",
                session.SessionId.ToString("D"),
                "Asia/Ho_Chi_Minh"));
        Assert.Equal(HttpStatusCode.OK, claimResponse.StatusCode);
        var claim = await claimResponse.Content.ReadFromJsonAsync<ClaimPairingResponse>(Json);
        Assert.NotNull(claim);
        Assert.StartsWith("efm_", claim.MobileToken);
        Assert.Equal("Student PC", claim.DesktopName);
        Assert.Equal("Asia/Ho_Chi_Minh", claim.TimeZoneId);

        var reused = await anonymous.PostAsJsonAsync(
            "/v1/pairing/claim",
            new ClaimPairingRequest(session.Code, "Other phone", "android"));
        Assert.Equal(HttpStatusCode.Gone, reused.StatusCode);
        Assert.Equal(
            "pairing_unavailable",
            (await reused.Content.ReadFromJsonAsync<ApiErrorResponse>(Json))!.Error.Code);

        var claimed = await desktop.GetFromJsonAsync<PairingSessionStatusResponse>(
            $"/v1/pairing/sessions/{session.SessionId}", Json);
        Assert.Equal(PairingSessionStatus.Claimed, claimed!.Status);
        Assert.Equal("Parent phone", claimed.MobileDeviceName);
        Assert.Equal(claim.DeviceId, claimed.MobileDeviceId);

        using var mobile = CreateAuthenticatedClient(factory, claim.MobileToken);
        var link = await mobile.GetFromJsonAsync<LinkStatusResponse>("/v1/link/status", Json);
        Assert.NotNull(link);
        Assert.True(link.Linked);
        Assert.Equal(claim.FamilyId, link.FamilyId);
        Assert.Equal(2, link.Devices.Count);

        var rotateResponse = await mobile.PostAsync("/v1/devices/token/rotate", null);
        Assert.Equal(HttpStatusCode.OK, rotateResponse.StatusCode);
        var rotated = await rotateResponse.Content.ReadFromJsonAsync<RotateDeviceTokenResponse>(Json);
        Assert.NotNull(rotated);
        Assert.NotEqual(claim.MobileToken, rotated.Token);

        Assert.Equal(
            HttpStatusCode.Unauthorized,
            (await mobile.GetAsync("/v1/link/status")).StatusCode);

        using var rotatedMobile = CreateAuthenticatedClient(factory, rotated.Token);
        Assert.Equal(HttpStatusCode.OK, (await rotatedMobile.GetAsync("/v1/link/status")).StatusCode);

        var revokeResponse = await desktop.DeleteAsync($"/v1/devices/{claim.DeviceId}");
        Assert.Equal(HttpStatusCode.OK, revokeResponse.StatusCode);
        Assert.Equal(
            HttpStatusCode.Unauthorized,
            (await rotatedMobile.GetAsync("/v1/link/status")).StatusCode);
    }

    [Fact]
    public async Task Session_status_is_scoped_to_the_creating_desktop()
    {
        var connectionString = Environment.GetEnvironmentVariable(TestDbVariable);
        if (string.IsNullOrWhiteSpace(connectionString))
        {
            return;
        }

        await ResetDatabaseAsync(connectionString);
        await using var factory = new CloudApiFactory(connectionString);
        using var anonymous = factory.CreateClient();

        var firstResponse = await anonymous.PostAsJsonAsync(
            "/v1/pairing/sessions",
            new CreatePairingSessionRequest("Student PC", "windows"));
        var first = (await firstResponse.Content.ReadFromJsonAsync<PairingSessionResponse>(Json))!;
        var secondResponse = await anonymous.PostAsJsonAsync(
            "/v1/pairing/sessions",
            new CreatePairingSessionRequest("Other PC", "windows"));
        var second = (await secondResponse.Content.ReadFromJsonAsync<PairingSessionResponse>(Json))!;

        using var secondDesktop = CreateAuthenticatedClient(factory, second.DesktopToken);
        var otherSession = await secondDesktop.GetAsync($"/v1/pairing/sessions/{first.SessionId}");
        Assert.Equal(HttpStatusCode.NotFound, otherSession.StatusCode);
    }

    private static HttpClient CreateAuthenticatedClient(CloudApiFactory factory, string token)
    {
        var client = factory.CreateClient();
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", token);
        return client;
    }

    private static async Task ResetDatabaseAsync(string connectionString)
    {
        await using var db = new CloudDbContext(
            new DbContextOptionsBuilder<CloudDbContext>().UseNpgsql(connectionString).Options);
        await db.Database.EnsureDeletedAsync();
        await db.Database.MigrateAsync();
    }
}
