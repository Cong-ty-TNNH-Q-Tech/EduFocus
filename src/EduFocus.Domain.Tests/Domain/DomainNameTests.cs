using EduFocus.Domain.ValueObjects;
using FluentAssertions;

namespace EduFocus.Domain.Tests.Domain;

public class DomainNameTests
{
    [Theory]
    [InlineData("Example.COM", "example.com")]
    [InlineData("  example.com.  ", "example.com")]
    [InlineData("https://Example.com/path?q=1#frag", "example.com")]
    [InlineData("https://user:pass@www.example.com:8443/Path", "www.example.com")]
    [InlineData("sub.example.com:443", "sub.example.com")]
    [InlineData("münchen.de", "xn--mnchen-3ya.de")]
    public void Parse_NormalizesHost(string input, string expected)
    {
        DomainName.Parse(input).Value.Should().Be(expected);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData("localhost")]
    [InlineData("example")]
    [InlineData("1.2.3.4")]
    [InlineData("-bad.com")]
    [InlineData("bad-.com")]
    [InlineData("bad_domain.com")]
    [InlineData("exa mple.com")]
    [InlineData("http://")]
    [InlineData("example..com")]
    public void TryParse_RejectsInvalidHosts(string? input)
    {
        DomainName.TryParse(input, out var domain).Should().BeFalse();
        domain.Should().BeNull();
    }

    [Fact]
    public void Parse_RejectsNull()
    {
        var act = () => DomainName.Parse(null);

        act.Should().Throw<ArgumentException>();
    }

    [Fact]
    public void IsSameOrSubdomainOf_RequiresLabelBoundary()
    {
        var parent = DomainName.Parse("example.com");

        DomainName.Parse("example.com").IsSameOrSubdomainOf(parent).Should().BeTrue();
        DomainName.Parse("sub.example.com").IsSameOrSubdomainOf(parent).Should().BeTrue();
        DomainName.Parse("notexample.com").IsSameOrSubdomainOf(parent).Should().BeFalse();
        DomainName.Parse("example.com.evil.com").IsSameOrSubdomainOf(parent).Should().BeFalse();
    }

    [Fact]
    public void Equality_IsCaseInsensitiveAfterNormalization()
    {
        var first = DomainName.Parse("Example.COM");
        var second = DomainName.Parse("example.com");

        first.Should().Be(second);
        first.GetHashCode().Should().Be(second.GetHashCode());
    }
}
