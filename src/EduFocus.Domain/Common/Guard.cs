namespace EduFocus.Domain.Common;

internal static class Guard
{
    public static void NotNullOrWhiteSpace(string? value, string parameterName)
    {
        if (string.IsNullOrWhiteSpace(value))
            throw new ArgumentException("Value is required.", parameterName);
    }

    public static void MaxLength(string? value, int maxLength, string parameterName)
    {
        if (value is not null && value.Length > maxLength)
            throw new ArgumentException($"Value must not exceed {maxLength} characters.", parameterName);
    }

    public static void InRange(int value, int min, int max, string parameterName)
    {
        if (value < min || value > max)
            throw new ArgumentOutOfRangeException(parameterName, value, $"Value must be between {min} and {max}.");
    }

    public static void NonNegative(int value, string parameterName)
    {
        if (value < 0)
            throw new ArgumentOutOfRangeException(parameterName, value, "Value must not be negative.");
    }

    public static void NonNegative(double value, string parameterName)
    {
        if (value < 0)
            throw new ArgumentOutOfRangeException(parameterName, value, "Value must not be negative.");
    }

    public static void Utc(DateTime value, string parameterName)
    {
        if (value.Kind != DateTimeKind.Utc)
            throw new ArgumentException("Timestamp must be UTC.", parameterName);
    }

    public static void Utc(DateTime? value, string parameterName)
    {
        if (value.HasValue)
            Utc(value.Value, parameterName);
    }

    public static void NotDefault<T>(T value, string parameterName) where T : struct
    {
        if (EqualityComparer<T>.Default.Equals(value, default))
            throw new ArgumentException("Value must not be empty.", parameterName);
    }

    public static void Defined<TEnum>(TEnum value, string parameterName) where TEnum : struct, Enum
    {
        if (!Enum.IsDefined(value))
            throw new ArgumentOutOfRangeException(parameterName, value, "Value is not a defined option.");
    }
}
