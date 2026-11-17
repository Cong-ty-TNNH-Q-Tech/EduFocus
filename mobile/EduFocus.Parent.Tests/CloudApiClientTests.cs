using System.Net;
using System.Net.Http.Headers;
using System.Text;
using EduFocus.Cloud.Contracts.Common;
using EduFocus.Cloud.Contracts.Pairing;
using EduFocus.Parent.Core;
using EduFocus.Parent.Core.Services;

namespace EduFocus.Parent.Tests;

public sealed class CloudApiClientTests
{
    [Fact]
    public async Task Get_uses_base_url_and_bearer_token()
    {
        var handler = new StubHttpMessageHandler(_ => JsonResponse(
            HttpStatusCode.OK, """{"status":"healthy","checkedAtUtc":"2026-11-17T08:00:00+00:00"}"""));
        var client = CreateClient(handler, baseUrl: "https://cloud.example.com/");

        var health = await client.GetAsync<HealthResponse>("/v1/health");

        Assert.Equal("healthy", health.Status);
        Assert.Equal("https://cloud.example.com/v1/health", handler.LastRequest?.RequestUri?.ToString());
        Assert.Equal("Bearer", handler.LastRequest?.Headers.Authorization?.Scheme);
        Assert.Equal("efm_test-token", handler.LastRequest?.Headers.Authorization?.Parameter);
    }

    [Fact]
    public async Task Post_serializes_camel_case_body()
    {
        string? requestBody = null;
        var handler = new StubHttpMessageHandler(request =>
        {
            requestBody = request.Content?.ReadAsStringAsync().GetAwaiter().GetResult();
            return JsonResponse(HttpStatusCode.OK, """
                {"mobileToken":"efm_token","familyId":"6f7d1a6e-2f4f-4e3b-9b8e-7c1f5c0a1b2c",
                 "deviceId":"0b5c9a5e-8f4a-4c6f-9f0b-2d3e4f5a6b7c","timeZone":"Asia/Ho_Chi_Minh",
                 "linkedAtUtc":"2026-11-17T08:00:00+00:00"}
                """);
        });
        var client = CreateClient(handler);

        var response = await client.PostAsync<ClaimPairingRequest, ClaimPairingResponse>(
            "/v1/pairing/claim",
            new ClaimPairingRequest("ABCD1234", "Parent phone", "android"),
            authenticated: false);

        Assert.Equal("efm_token", response.MobileToken);
        Assert.Contains("\"code\":\"ABCD1234\"", requestBody);
        Assert.Contains("\"deviceName\":\"Parent phone\"", requestBody);
        Assert.Null(handler.LastRequest?.Headers.Authorization);
    }

    [Fact]
    public async Task Error_envelope_maps_to_coded_exception()
    {
        var handler = new StubHttpMessageHandler(_ => JsonResponse(
            HttpStatusCode.Unauthorized,
            """{"error":{"code":"invalid_token","message":"Mã truy cập không hợp lệ.","traceId":"trace-1"}}"""));
        var client = CreateClient(handler);

        var exception = await Assert.ThrowsAsync<CloudApiException>(
            () => client.GetAsync<HealthResponse>("/v1/health"));

        Assert.Equal(401, exception.StatusCode);
        Assert.Equal("invalid_token", exception.Code);
        Assert.Equal("Mã truy cập không hợp lệ.", exception.Message);
        Assert.Equal("trace-1", exception.TraceId);
    }

    [Fact]
    public async Task Problem_details_maps_to_exception()
    {
        var handler = new StubHttpMessageHandler(_ => JsonResponse(
            HttpStatusCode.BadRequest,
            """{"title":"Bad Request","detail":"Mã ghép nối không hợp lệ.","status":400}"""));
        var client = CreateClient(handler);

        var exception = await Assert.ThrowsAsync<CloudApiException>(
            () => client.PostAsync("/v1/pairing/claim", new { code = "XXXX" }, authenticated: false));

        Assert.Equal("http_400", exception.Code);
        Assert.Equal("Mã ghép nối không hợp lệ.", exception.Message);
    }

    [Fact]
    public async Task Missing_base_url_throws_before_calling_cloud()
    {
        var handler = new StubHttpMessageHandler(_ => throw new InvalidOperationException("must not call"));
        var client = CreateClient(handler, baseUrl: null);

        var exception = await Assert.ThrowsAsync<CloudApiException>(
            () => client.GetAsync<HealthResponse>("/v1/health"));

        Assert.Equal("cloud_not_linked", exception.Code);
        Assert.Null(handler.LastRequest);
    }

    [Fact]
    public async Task Missing_token_throws_for_authenticated_calls()
    {
        var handler = new StubHttpMessageHandler(_ => JsonResponse(HttpStatusCode.OK, "{}"));
        var client = CreateClient(handler, token: null);

        var exception = await Assert.ThrowsAsync<CloudApiException>(
            () => client.GetAsync<HealthResponse>("/v1/reports/daily"));

        Assert.Equal("missing_token", exception.Code);
        Assert.Null(handler.LastRequest);
    }

    [Fact]
    public async Task No_content_response_completes_without_error()
    {
        var handler = new StubHttpMessageHandler(_ => new HttpResponseMessage(HttpStatusCode.NoContent));
        var client = CreateClient(handler);

        await client.DeleteAsync("/v1/devices/1");
    }

    private static CloudApiClient CreateClient(
        StubHttpMessageHandler handler,
        string? baseUrl = "https://cloud.example.com",
        string? token = "efm_test-token")
    {
        var state = new AppState { CloudBaseUrl = baseUrl };
        return new CloudApiClient(new StubHttpClientFactory(handler), new StubTokenStore(token), state);
    }

    private static HttpResponseMessage JsonResponse(HttpStatusCode statusCode, string json) =>
        new(statusCode)
        {
            Content = new StringContent(json, Encoding.UTF8, "application/json"),
        };

    private sealed class StubHttpMessageHandler(Func<HttpRequestMessage, HttpResponseMessage> responder)
        : HttpMessageHandler
    {
        public HttpRequestMessage? LastRequest { get; private set; }

        protected override Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request,
            CancellationToken cancellationToken)
        {
            LastRequest = request;
            return Task.FromResult(responder(request));
        }
    }

    private sealed class StubHttpClientFactory(HttpMessageHandler handler) : IHttpClientFactory
    {
        public HttpClient CreateClient(string name) => new(handler, disposeHandler: false);
    }

    private sealed class StubTokenStore(string? token) : ITokenStore
    {
        public Task<string?> GetTokenAsync(CancellationToken cancellationToken = default) =>
            Task.FromResult(token);

        public Task SaveTokenAsync(string value, CancellationToken cancellationToken = default)
        {
            throw new NotSupportedException();
        }

        public Task ClearAsync(CancellationToken cancellationToken = default) =>
            throw new NotSupportedException();
    }
}
