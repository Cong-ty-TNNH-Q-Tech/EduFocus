namespace EduFocus.Cloud.Api.Common;

public static class TimeZones
{
    public const string Default = "Asia/Ho_Chi_Minh";

    public static bool TryNormalize(string? value, out string timeZoneId)
    {
        timeZoneId = Default;
        if (string.IsNullOrWhiteSpace(value))
        {
            return true;
        }

        var candidate = value.Trim();
        if (!TimeZoneInfo.TryFindSystemTimeZoneById(candidate, out _))
        {
            return false;
        }

        timeZoneId = candidate;
        return true;
    }
}
