using EduFocus.Domain.Enums;
using Microsoft.EntityFrameworkCore.Storage.ValueConversion;

namespace EduFocus.SqliteDataAccess.Converters;

public sealed class AppThemeConverter : ValueConverter<AppTheme, string>
{
    public AppThemeConverter()
        : base(value => ToStorage(value), value => FromStorage(value))
    {
    }

    private static string ToStorage(AppTheme value) => value switch
    {
        AppTheme.Dark => "dark",
        AppTheme.Light => "light",
        AppTheme.System => "system",
        _ => throw new ArgumentOutOfRangeException(nameof(value), value, "Unknown theme value.")
    };

    private static AppTheme FromStorage(string value) => value switch
    {
        "dark" => AppTheme.Dark,
        "light" => AppTheme.Light,
        "system" => AppTheme.System,
        _ => throw new ArgumentOutOfRangeException(nameof(value), value, "Unknown theme value.")
    };
}

public sealed class AppLanguageConverter : ValueConverter<AppLanguage, string>
{
    public AppLanguageConverter()
        : base(value => ToStorage(value), value => FromStorage(value))
    {
    }

    private static string ToStorage(AppLanguage value) => value switch
    {
        AppLanguage.Vi => "vi",
        AppLanguage.En => "en",
        _ => throw new ArgumentOutOfRangeException(nameof(value), value, "Unknown language value.")
    };

    private static AppLanguage FromStorage(string value) => value switch
    {
        "vi" => AppLanguage.Vi,
        "en" => AppLanguage.En,
        _ => throw new ArgumentOutOfRangeException(nameof(value), value, "Unknown language value.")
    };
}
