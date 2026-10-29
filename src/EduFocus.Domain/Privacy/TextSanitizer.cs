namespace EduFocus.Domain.Privacy;

/// <summary>
/// Cleans free text that is persisted (window titles, notes). Control characters
/// become spaces so values stay single-line; results are trimmed and truncated.
/// </summary>
public static class TextSanitizer
{
    public static string? Clean(string? value, int maxLength)
    {
        if (string.IsNullOrWhiteSpace(value))
            return null;

        var buffer = value.Trim().ToCharArray();
        for (var i = 0; i < buffer.Length; i++)
        {
            if (char.IsControl(buffer[i]))
                buffer[i] = ' ';
        }

        var text = new string(buffer).Trim();
        if (text.Length > maxLength)
            text = text[..maxLength].TrimEnd();

        return text.Length == 0 ? null : text;
    }
}
