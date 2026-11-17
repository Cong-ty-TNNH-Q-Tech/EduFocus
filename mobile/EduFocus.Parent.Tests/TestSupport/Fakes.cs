using System.Net;
using System.Text;
using EduFocus.Parent.Core.Services;
using EduFocus.Parent.Core.Settings;

namespace EduFocus.Parent.Tests.TestSupport;

internal sealed class FakeKeyValueStore : IKeyValueStore
{
    private readonly Dictionary<string, string> _values = new(StringComparer.Ordinal);

    public string? Get(string key) => _values.TryGetValue(key, out var value) ? value : null;

    public void Set(string key, string value) => _values[key] = value;

    public void Remove(string key) => _values.Remove(key);
}

internal sealed class MemoryTokenStore(string? token = null) : ITokenStore
{
    public string? Token { get; private set; } = token;

    public int ClearCalls { get; private set; }

    public Task<string?> GetTokenAsync(CancellationToken cancellationToken = default) =>
        Task.FromResult(Token);

    public Task SaveTokenAsync(string token, CancellationToken cancellationToken = default)
    {
        Token = token;
        return Task.CompletedTask;
    }

    public Task ClearAsync(CancellationToken cancellationToken = default)
    {
        Token = null;
        ClearCalls++;
        return Task.CompletedTask;
    }
}

internal sealed class RecordingHttpMessageHandler(
    Func<HttpRequestMessage, HttpResponseMessage> responder) : HttpMessageHandler
{
    public List<HttpRequestMessage> Requests { get; } = [];

    public List<string> RequestBodies { get; } = [];

    public HttpRequestMessage? LastRequest => Requests.Count == 0 ? null : Requests[^1];

    protected override async Task<HttpResponseMessage> SendAsync(
        HttpRequestMessage request,
        CancellationToken cancellationToken)
    {
        Requests.Add(request);
        RequestBodies.Add(request.Content is null
            ? string.Empty
            : await request.Content.ReadAsStringAsync(cancellationToken));
        return responder(request);
    }
}

internal sealed class RecordingHttpClientFactory(HttpMessageHandler handler) : IHttpClientFactory
{
    public HttpClient CreateClient(string name) => new(handler, disposeHandler: false);
}

internal static class HttpStubs
{
    public static HttpResponseMessage Json(HttpStatusCode statusCode, string json)
    {
        var response = new HttpResponseMessage(statusCode)
        {
            Content = new StringContent(json, Encoding.UTF8, "application/json"),
        };
        return response;
    }

    public static HttpResponseMessage RateLimited(string json, int retryAfterSeconds)
    {
        var response = Json(HttpStatusCode.TooManyRequests, json);
        response.Headers.RetryAfter = new System.Net.Http.Headers.RetryConditionHeaderValue(
            TimeSpan.FromSeconds(retryAfterSeconds));
        return response;
    }
}
