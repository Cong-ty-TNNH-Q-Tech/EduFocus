using System.Globalization;
using Microsoft.EntityFrameworkCore.Storage.ValueConversion;

namespace EduFocus.SqliteDataAccess.Converters;

/// <summary>
/// Stores UTC timestamps as ISO-8601 text with milliseconds and a trailing Z
/// (database_schema §3) so ordering in SQLite stays lexicographic.
/// </summary>
public sealed class UtcDateTimeConverter : ValueConverter<DateTime, string>
{
    public const string Format = "yyyy-MM-dd'T'HH:mm:ss.fff'Z'";

    public UtcDateTimeConverter()
        : base(value => ToStorage(value), value => FromStorage(value))
    {
    }

    internal static string ToStorage(DateTime value) =>
        value.ToUniversalTime().ToString(Format, CultureInfo.InvariantCulture);

    internal static DateTime FromStorage(string value) =>
        DateTime.ParseExact(
            value,
            Format,
            CultureInfo.InvariantCulture,
            DateTimeStyles.AssumeUniversal | DateTimeStyles.AdjustToUniversal);
}

public sealed class NullableUtcDateTimeConverter : ValueConverter<DateTime?, string?>
{
    public NullableUtcDateTimeConverter()
        : base(
            value => value == null ? null : UtcDateTimeConverter.ToStorage(value.Value),
            value => value == null ? null : UtcDateTimeConverter.FromStorage(value))
    {
    }
}
