using EduFocus.Parent.Core.Services;

namespace EduFocus.Parent.Core.Presentation;

public static class CloudErrors
{
    public const string SessionExpired = "Phiên ghép nối đã hết, vui lòng ghép lại.";

    public static bool IsUnauthorized(Exception exception) =>
        exception is CloudApiException { StatusCode: 401 };

    public static string Describe(Exception exception) => exception switch
    {
        CloudApiException { StatusCode: 401 } => SessionExpired,
        CloudApiException { StatusCode: 429 } =>
            "Cloud đang giới hạn yêu cầu. Vui lòng thử lại sau ít phút.",
        CloudApiException { StatusCode: >= 500 } =>
            "Máy chủ cloud đang gặp sự cố. Vui lòng thử lại sau.",
        CloudApiException api when !string.IsNullOrWhiteSpace(api.Message) => api.Message,
        HttpRequestException or TaskCanceledException =>
            "Không kết nối được cloud. Kiểm tra mạng và thử lại.",
        _ => "Không tải được dữ liệu. Vui lòng thử lại.",
    };
}
