using System.Globalization;

namespace EduFocus.Domain.ValueObjects;

/// <summary>
/// Normalized host name. Accepts raw hosts or URL-like input and keeps only the
/// lowercase ASCII host, without scheme, credentials, port, path, query or fragment.
/// </summary>
public sealed class DomainName : IEquatable<DomainName>
{
    private static readonly IdnMapping Idn = new();

    private readonly string _value;

    private DomainName(string value) => _value = value;

    public string Value => _value;

    public static DomainName Parse(string? input)
    {
        if (!TryParse(input, out var domain))
            throw new ArgumentException("Value is not a valid domain name.", nameof(input));

        return domain!;
    }

    public static bool TryParse(string? input, out DomainName? domain)
    {
        domain = null;
        if (string.IsNullOrWhiteSpace(input))
            return false;

        var text = input.Trim();

        if (text.Contains("://", StringComparison.Ordinal))
        {
            if (!Uri.TryCreate(text, UriKind.Absolute, out var uri) || string.IsNullOrEmpty(uri.Host))
                return false;

            text = uri.Host;
        }
        else
        {
            var at = text.LastIndexOf('@');
            if (at >= 0)
                text = text[(at + 1)..];

            var pathStart = text.IndexOfAny(['/', '?', '#']);
            if (pathStart >= 0)
                text = text[..pathStart];

            text = StripPort(text);
        }

        text = text.TrimEnd('.');
        if (text.Length == 0 || text.Length > 253)
            return false;

        string ascii;
        try
        {
            ascii = Idn.GetAscii(text).ToLowerInvariant();
        }
        catch (ArgumentException)
        {
            return false;
        }

        var labels = ascii.Split('.');
        if (labels.Length < 2)
            return false;

        foreach (var label in labels)
        {
            if (label.Length is < 1 or > 63)
                return false;
            if (label[0] == '-' || label[^1] == '-')
                return false;

            foreach (var c in label)
            {
                if (!char.IsAsciiLetterOrDigit(c) && c != '-')
                    return false;
            }
        }

        if (labels[^1].All(char.IsAsciiDigit))
            return false;

        domain = new DomainName(ascii);
        return true;
    }

    /// <summary>True when this host is the given host or one of its subdomains.</summary>
    public bool IsSameOrSubdomainOf(DomainName parent)
    {
        ArgumentNullException.ThrowIfNull(parent);
        return _value.Equals(parent._value, StringComparison.Ordinal)
            || _value.EndsWith("." + parent._value, StringComparison.Ordinal);
    }

    public bool Equals(DomainName? other) =>
        other is not null && _value.Equals(other._value, StringComparison.Ordinal);

    public override bool Equals(object? obj) => obj is DomainName other && Equals(other);

    public override int GetHashCode() => StringComparer.Ordinal.GetHashCode(_value);

    public override string ToString() => _value;

    private static string StripPort(string text)
    {
        var colon = text.LastIndexOf(':');
        if (colon < 0)
            return text;

        var port = text[(colon + 1)..];
        return port.Length > 0 && port.All(char.IsAsciiDigit) ? text[..colon] : text;
    }
}
