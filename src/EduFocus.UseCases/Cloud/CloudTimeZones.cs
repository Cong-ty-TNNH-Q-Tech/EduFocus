namespace EduFocus.UseCases.Cloud;

/// <summary>
/// Normalizes the desktop zone id for the cloud relay. The server resolves
/// IANA ids, while Windows reports ids such as "SE Asia Standard Time", so the
/// payload only carries a convertible id and falls back to null (the family
/// zone) otherwise.
/// </summary>
internal static class CloudTimeZones
{
    private const int MaxIdLength = 64;

    public static string? ToSendableId(TimeZoneInfo zone)
    {
        ArgumentNullException.ThrowIfNull(zone);

        var id = zone.Id?.Trim();
        if (string.IsNullOrEmpty(id))
            return null;

        if (TimeZoneInfo.TryConvertWindowsIdToIanaId(id, out var iana)
            && !string.IsNullOrEmpty(iana)
            && iana.Length <= MaxIdLength)
        {
            return iana;
        }

        if (id.Length <= MaxIdLength
            && id.Contains('/')
            && TimeZoneInfo.TryFindSystemTimeZoneById(id, out _))
        {
            return id;
        }

        return null;
    }
}
