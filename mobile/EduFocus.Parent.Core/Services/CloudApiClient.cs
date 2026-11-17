using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using EduFocus.Cloud.Contracts.Common;
using EduFocus.Cloud.Contracts.Json;

namespace EduFocus.Parent.Core.Services;

public interface ICloudApiClient
{
    Task<TResponse> GetAsync<TResponse>(
        string relativePath,
        bool authenticated = true,
        CancellationToken cancellationToken = default);

    Task<TResponse> PostAsync<TBody, TResponse>(
        string relativePath,
        TBody body,
        bool authenticated = true,
        CancellationToken cancellationToken = default);

    Task PostAsync<TBody>(
        string relativePath,
        TBody body,
        bool authenticated = true,
        CancellationToken cancellationToken = default);

    Task DeleteAsync(
        string relativePath,
        bool authenticated = true,
        CancellationToken cancellationToken = default);
}

public sealed class CloudApiClient(
    IHttpClientFactory httpClientFactory,
    ITokenStore tokenStore,
    AppState appState) : ICloudApiClient
{
    public const string HttpClientName = "cloud";
    private const int MaxErrorBodyLength = 300;

    public Task<TResponse> GetAsync<TResponse>(
        string relativePath,
        bool authenticated = true,
        CancellationToken cancellationToken = default) =>
        SendAsync<TResponse>(HttpMethod.Get, relativePath, null, authenticated, cancellationToken);

    public Task<TResponse> PostAsync<TBody, TResponse>(
        string relativePath,
        TBody body,
        bool authenticated = true,
        CancellationToken cancellationToken = default) =>
        SendAsync<TResponse>(HttpMethod.Post, relativePath, body, authenticated, cancellationToken);

    public async Task PostAsync<TBody>(
        string relativePath,
        TBody body,
        bool authenticated = true,
        CancellationToken cancellationToken = default)
    {
        await SendAsync<object>(HttpMethod.Post, relativePath, body, authenticated, cancellationToken);
    }

    public async Task DeleteAsync(
        string relativePath,
        bool authenticated = true,
        CancellationToken cancellationToken = default)
    {
        await SendAsync<object>(HttpMethod.Delete, relativePath, null, authenticated, cancellationToken);
    }

    private async Task<TResponse> SendAsync<TResponse>(
        HttpMethod method,
        string relativePath,
        object? body,
        bool authenticated,
        CancellationToken cancellationToken)
    {
        using var request = await CreateRequestAsync(method, relativePath, body, authenticated, cancellationToken);
        using var response = await httpClientFactory
            .CreateClient(HttpClientName)
            .SendAsync(request, HttpCompletionOption.ResponseHeadersRead, cancellationToken);

        if (!response.IsSuccessStatusCode)
        {
            throw await CreateExceptionAsync(response, cancellationToken);
        }

        if (response.StatusCode == HttpStatusCode.NoContent || response.Content.Headers.ContentLength == 0)
        {
            return default!;
        }

        await using var stream = await response.Content.ReadAsStreamAsync(cancellationToken);
        var result = await JsonSerializer.DeserializeAsync<TResponse>(stream, ApiJson.Options, cancellationToken);
        return result!;
    }

    private async Task<HttpRequestMessage> CreateRequestAsync(
        HttpMethod method,
        string relativePath,
        object? body,
        bool authenticated,
        CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(appState.CloudBaseUrl)
            || !Uri.TryCreate(appState.CloudBaseUrl, UriKind.Absolute, out var baseUri))
        {
            throw new CloudApiException(0, "cloud_not_linked", "Máy chưa được ghép nối với cloud.");
        }

        var request = new HttpRequestMessage(method, new Uri(baseUri, relativePath));
        if (body is not null)
        {
            request.Content = JsonContent.Create(body, options: ApiJson.Options);
        }

        if (authenticated)
        {
            var token = await tokenStore.GetTokenAsync(cancellationToken);
            if (string.IsNullOrWhiteSpace(token))
            {
                throw new CloudApiException(0, "missing_token", "Chưa có mã truy cập cloud. Hãy ghép nối lại.");
            }

            request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);
        }

        return request;
    }

    private static async Task<CloudApiException> CreateExceptionAsync(
        HttpResponseMessage response,
        CancellationToken cancellationToken)
    {
        var statusCode = (int)response.StatusCode;
        string code = $"http_{statusCode}";
        string message = "Không kết nối được cloud. Vui lòng thử lại.";
        string? traceId = null;

        try
        {
            var body = await response.Content.ReadAsStringAsync(cancellationToken);
            if (!string.IsNullOrWhiteSpace(body))
            {
                var envelope = JsonSerializer.Deserialize<ApiErrorResponse>(body, ApiJson.Options);
                if (envelope?.Error is { } error && !string.IsNullOrWhiteSpace(error.Code))
                {
                    code = error.Code;
                    if (!string.IsNullOrWhiteSpace(error.Message))
                        message = error.Message;
                    traceId = error.TraceId;
                }
                else
                {
                    var problem = JsonSerializer.Deserialize<ProblemDetailsBody>(body, ApiJson.Options);
                    if (!string.IsNullOrWhiteSpace(problem?.Detail) || !string.IsNullOrWhiteSpace(problem?.Title))
                    {
                        message = problem.Detail ?? problem.Title!;
                        traceId = problem.TraceId;
                    }
                }
            }
        }
        catch (JsonException)
        {
            message = "Cloud trả về lỗi không đọc được. Vui lòng thử lại.";
        }

        return new CloudApiException(statusCode, code, Truncate(message), traceId, RetryAfterOf(response));
    }

    private static TimeSpan? RetryAfterOf(HttpResponseMessage response)
    {
        var retryAfter = response.Headers.RetryAfter;
        if (retryAfter?.Delta is { } delta && delta > TimeSpan.Zero)
            return delta;

        if (retryAfter?.Date is { } date)
        {
            var delay = date - DateTimeOffset.UtcNow;
            if (delay > TimeSpan.Zero)
                return delay;
        }

        return null;
    }

    private static string Truncate(string value) =>
        value.Length <= MaxErrorBodyLength ? value : value[..MaxErrorBodyLength];

    private sealed record ProblemDetailsBody(string? Title, string? Detail, int? Status, string? TraceId);
}
