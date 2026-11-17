namespace EduFocus.Ports.Outbound.Cloud;

/// <summary>
/// Safe failure from the cloud relay. The message is user-facing and must not
/// contain the bearer token, pairing code or raw response body; only the code
/// and HTTP status are suitable for diagnostics.
/// </summary>
public sealed class CloudSyncException : Exception
{
    public CloudSyncException(
        string code,
        string message,
        bool isNetworkError = false,
        int? statusCode = null,
        TimeSpan? retryAfter = null)
        : base(message)
    {
        Code = code;
        IsNetworkError = isNetworkError;
        StatusCode = statusCode;
        RetryAfter = retryAfter;
    }

    public string Code { get; }

    public bool IsNetworkError { get; }

    public int? StatusCode { get; }

    /// <summary>Server requested delay for 429 responses, when provided.</summary>
    public TimeSpan? RetryAfter { get; }
}
