using System.Security.Cryptography;
using System.Text;

namespace EduFocus.Cloud.Api.Pairing;

public static class PairingCode
{
    public const string Alphabet = "0123456789ABCDEFGHJKMNPQRSTVWXYZ";
    public const int Length = 8;

    public static string Create()
    {
        Span<char> chars = stackalloc char[Length];
        for (var i = 0; i < chars.Length; i++)
        {
            chars[i] = Alphabet[RandomNumberGenerator.GetInt32(Alphabet.Length)];
        }

        return new string(chars);
    }

    public static bool TryNormalize(string? value, out string normalized)
    {
        normalized = string.Empty;
        if (string.IsNullOrWhiteSpace(value))
        {
            return false;
        }

        var builder = new StringBuilder(Length);
        foreach (var character in value.Trim().ToUpperInvariant())
        {
            if (character is ' ' or '-')
            {
                continue;
            }

            var candidate = character switch
            {
                'I' or 'L' => '1',
                'O' => '0',
                _ => character,
            };

            if (!Alphabet.Contains(candidate))
            {
                return false;
            }

            builder.Append(candidate);
        }

        if (builder.Length != Length)
        {
            return false;
        }

        normalized = builder.ToString();
        return true;
    }

    public static string Hash(string normalizedCode) =>
        Convert.ToHexStringLower(SHA256.HashData(Encoding.UTF8.GetBytes(normalizedCode)));
}
