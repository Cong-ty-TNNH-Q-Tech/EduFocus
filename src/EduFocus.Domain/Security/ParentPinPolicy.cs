namespace EduFocus.Domain.Security;

/// <summary>
/// PIN rules from system architecture §9.1: 6–8 digits, no repeated digit and
/// no strictly ascending or descending sequence.
/// </summary>
public static class ParentPinPolicy
{
    public const int MinLength = 6;
    public const int MaxLength = 8;

    public static bool IsValid(string? pin)
    {
        if (pin is null || pin.Length < MinLength || pin.Length > MaxLength)
            return false;

        if (!pin.All(char.IsAsciiDigit))
            return false;

        if (pin.Distinct().Count() == 1)
            return false;

        return !IsSequential(pin, 1) && !IsSequential(pin, -1);
    }

    public static void EnsureValid(string? pin, string parameterName = "pin")
    {
        if (!IsValid(pin))
            throw new ArgumentException(
                "PIN must be 6–8 digits and must not be a repeated or sequential pattern.",
                parameterName);
    }

    private static bool IsSequential(string pin, int step)
    {
        for (var i = 1; i < pin.Length; i++)
        {
            if (pin[i] - pin[i - 1] != step)
                return false;
        }

        return true;
    }
}
