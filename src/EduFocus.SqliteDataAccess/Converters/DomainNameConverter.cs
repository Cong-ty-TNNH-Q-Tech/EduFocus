using EduFocus.Domain.ValueObjects;
using Microsoft.EntityFrameworkCore.Storage.ValueConversion;

namespace EduFocus.SqliteDataAccess.Converters;

public sealed class DomainNameConverter : ValueConverter<DomainName, string>
{
    public DomainNameConverter()
        : base(value => value.Value, value => DomainName.Parse(value))
    {
    }
}
