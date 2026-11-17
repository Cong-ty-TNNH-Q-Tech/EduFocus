namespace EduFocus.Parent.Core.Services;

public sealed class CloudApiException : Exception
{
    public CloudApiException(
        int statusCode,
        string code,
        string message,
        string? traceId = null,
        Exception? innerException = null)
        : base(message, innerException)
    {
        StatusCode = statusCode;
        Code = code;
        TraceId = traceId;
    }

    public int StatusCode { get; }

    public string Code { get; }

    public string? TraceId { get; }
}
