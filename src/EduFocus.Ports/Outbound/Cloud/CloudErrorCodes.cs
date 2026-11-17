namespace EduFocus.Ports.Outbound.Cloud;

/// <summary>
/// Stable error codes surfaced by <see cref="ICloudSyncPort"/> implementations.
/// Codes are logged; messages are user-facing Vietnamese and never contain
/// tokens, pairing codes or response bodies.
/// </summary>
public static class CloudErrorCodes
{
    public const string Network = "network";
    public const string Timeout = "timeout";
    public const string Unauthorized = "unauthorized";
    public const string NotFound = "not_found";
    public const string InvalidResponse = "invalid_response";
    public const string ServerError = "server_error";
    public const string RequestFailed = "request_failed";
}
