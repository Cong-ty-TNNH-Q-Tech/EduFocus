using System.Text;

namespace EduFocus.Parent.Core.Pairing;

/// <summary>
/// Normalises a pairing code typed by hand. Mirrors the server rule: 8 Crockford
/// base32 characters after removing spaces/dashes and folding I/L to 1, O to 0.
/// </summary>
public static class PairingCodeInput
{
    public const int Length = 8;
    public const string Alphabet = "0123456789ABCDEFGHJKMNPQRSTVWXYZ";
    public const string Hint = "Mã ghép nối gồm 8 ký tự (chữ và số), ví dụ ABCD-2345.";

    public static bool TryNormalize(string? value, out string normalized)
    {
        normalized = string.Empty;
        if (string.IsNullOrWhiteSpace(value))
            return false;

        var builder = new StringBuilder(Length);
        foreach (var character in value.Trim().ToUpperInvariant())
        {
            if (character is ' ' or '-')
                continue;

            var candidate = character switch
            {
                'I' or 'L' => '1',
                'O' => '0',
                _ => character,
            };

            if (!Alphabet.Contains(candidate))
                return false;

            builder.Append(candidate);
        }

        if (builder.Length != Length)
            return false;

        normalized = builder.ToString();
        return true;
    }
}
