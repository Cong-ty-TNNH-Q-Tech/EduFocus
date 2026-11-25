using Microsoft.EntityFrameworkCore.Storage.ValueConversion;

namespace EduFocus.Cloud.Data.Converters;

public sealed class DateTimeOffsetToUtcTicksConverter()
    : ValueConverter<DateTimeOffset, long>(
        value => value.ToUniversalTime().UtcTicks,
        value => new DateTimeOffset(value, TimeSpan.Zero));

public sealed class NullableDateTimeOffsetToUtcTicksConverter()
    : ValueConverter<DateTimeOffset?, long?>(
        value => value.HasValue ? value.Value.ToUniversalTime().UtcTicks : null,
        value => value.HasValue ? new DateTimeOffset(value.Value, TimeSpan.Zero) : null);
