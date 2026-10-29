using System.Text;

namespace EduFocus.Domain.Security;

/// <summary>
/// Recovery-code format from system architecture §9.1: 12 characters from an
/// unambiguous alphabet. Case, hyphens and whitespace are ignored on input so
/// only the canonical uppercase form is ever hashed or compared.
/// </summary>
public static class ParentRecoveryCode
{
    public const int Length = 12;
    public const string Alphabet = "ABCDEFGHJKLMNPQRSTUVWXYZ23456789";

    public static string Normalize(string? code)
    {
        if (string.IsNullOrWhiteSpace(code))
            return string.Empty;

        var builder = new StringBuilder(code.Length);
        foreach (var character in code)
        {
            if (character == '-' || char.IsWhiteSpace(character))
                continue;

            builder.Append(char.ToUpperInvariant(character));
        }

        return builder.ToString();
    }

    public static bool IsValid(string? code)
    {
        var normalized = Normalize(code);
        if (normalized.Length != Length)
            return false;

        foreach (var character in normalized)
        {
            if (Alphabet.IndexOf(character) < 0)
                return false;
        }

        return true;
    }
}
