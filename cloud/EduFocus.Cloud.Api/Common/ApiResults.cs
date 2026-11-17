using System.Diagnostics;
using EduFocus.Cloud.Contracts.Common;

namespace EduFocus.Cloud.Api.Common;

public static class ApiResults
{
    public static IResult Error(int statusCode, string code, string message) =>
        Results.Json(
            new ApiErrorResponse(new ApiError(code, message, Activity.Current?.Id)),
            statusCode: statusCode);

    public static IResult InvalidRequest(string message) =>
        Error(StatusCodes.Status400BadRequest, "invalid_request", message);

    public static IResult InvalidTimeZone() =>
        Error(StatusCodes.Status400BadRequest, "invalid_time_zone", "Múi giờ không hợp lệ.");

    public static IResult PairingUnavailable() =>
        Error(
            StatusCodes.Status410Gone,
            "pairing_unavailable",
            "Mã ghép nối không hợp lệ, đã dùng hoặc đã hết hạn.");

    public static IResult Forbidden(string message) =>
        Error(StatusCodes.Status403Forbidden, "forbidden", message);

    public static IResult NotFound() =>
        Error(StatusCodes.Status404NotFound, "not_found", "Không tìm thấy dữ liệu.");
}
