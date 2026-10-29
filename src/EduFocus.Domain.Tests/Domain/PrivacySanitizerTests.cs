using EduFocus.Domain.Privacy;
using FluentAssertions;

namespace EduFocus.Domain.Tests.Domain;

public class PrivacySanitizerTests
{
    [Fact]
    public void SanitizeUrl_RemovesCredentialsQueryAndFragment()
    {
        var result = UrlSanitizer.Sanitize("https://user:pw@Example.com:443/Path?token=abc#frag");

        result.Should().Be("https://example.com/Path");
    }

    [Fact]
    public void SanitizeUrl_RemovesQueryString()
    {
        var result = UrlSanitizer.Sanitize("https://example.com/search?q=private+query");

        result.Should().Be("https://example.com/search");
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData("not a url")]
    [InlineData("ftp://example.com/file")]
    [InlineData("javascript:alert(1)")]
    [InlineData("file:///C:/secret.txt")]
    public void SanitizeUrl_RejectsUnsupportedInput(string? url)
    {
        UrlSanitizer.Sanitize(url).Should().BeNull();
    }

    [Fact]
    public void DeriveDomain_UsesSanitizedHost()
    {
        UrlSanitizer.DeriveDomain("https://www.example.com/class?token=abc")!.Value
            .Should().Be("www.example.com");
    }

    [Fact]
    public void DeriveDomain_ReturnsNullWhenUrlIsUsable()
    {
        UrlSanitizer.DeriveDomain("mailto:teacher@example.com").Should().BeNull();
        UrlSanitizer.DeriveDomain(null).Should().BeNull();
    }

    [Fact]
    public void CleanText_ReplacesControlCharactersAndTrims()
    {
        var result = TextSanitizer.Clean("  Hello\r\nWorld\t ", 100);

        result.Should().Be("Hello  World");
        result.Should().NotContain("\r").And.NotContain("\n").And.NotContain("\t");
    }

    [Fact]
    public void CleanText_TruncatesToMaximumLength()
    {
        var result = TextSanitizer.Clean(new string('a', 50), 20);

        result.Should().HaveLength(20);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    public void CleanText_ReturnsNullForBlankValues(string? value)
    {
        TextSanitizer.Clean(value, 100).Should().BeNull();
    }
}
