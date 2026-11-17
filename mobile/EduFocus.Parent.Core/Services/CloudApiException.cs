namespace EduFocus.Parent.Core.Services;

public sealed class CloudApiException : Exception
{
    public CloudApiException(
        int statusCode,
        string code,
        string message,
        string? traceId = null,
        TimeSpan? retryAfter = null,
        Exception? innerException = null)
        : base(message, innerException)
    {
        StatusCode = statusCode;
        Code = code;
        TraceId = traceId;
        RetryAfter = retryAfter;
    }

    public int StatusCode { get; }

    public string Code { get; }

    public string? TraceId { get; }

    /// <summary>Server requested delay for 429 responses, when provided.</summary>
    public TimeSpan? RetryAfter { get; }
}
