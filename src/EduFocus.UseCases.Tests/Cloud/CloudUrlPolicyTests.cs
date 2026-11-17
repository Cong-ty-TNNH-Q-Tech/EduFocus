using EduFocus.UseCases.Cloud;
using FluentAssertions;

namespace EduFocus.UseCases.Tests.Cloud;

public sealed class CloudUrlPolicyTests
{
    [Theory]
    [InlineData("https://cloud.example.com", "https://cloud.example.com")]
    [InlineData("https://cloud.example.com/", "https://cloud.example.com")]
    [InlineData("  https://cloud.example.com  ", "https://cloud.example.com")]
    [InlineData("https://cloud.example.com:8443", "https://cloud.example.com:8443")]
    [InlineData("HTTPS://Cloud.Example.COM", "https://cloud.example.com")]
    public void Https_endpoints_are_accepted_and_normalized(string input, string expected)
    {
        var ok = CloudUrlPolicy.TryNormalize(input, allowInsecureDevelopmentEndpoints: false, out var url, out var error);

        ok.Should().BeTrue();
        url!.ToString().TrimEnd('/').Should().Be(expected);
        error.Should().BeNull();
    }

    [Theory]
    [InlineData("http://localhost:8080", "http://localhost:8080")]
    [InlineData("http://127.0.0.1:8080", "http://127.0.0.1:8080")]
    [InlineData("http://192.168.1.20:8080", "http://192.168.1.20:8080")]
    [InlineData("http://10.0.0.5", "http://10.0.0.5")]
    [InlineData("http://172.16.4.4", "http://172.16.4.4")]
    [InlineData("http://[::1]:8080", "http://[::1]:8080")]
    public void Insecure_http_is_allowed_only_for_local_or_lan_hosts_in_development(string input, string expected)
    {
        var ok = CloudUrlPolicy.TryNormalize(input, allowInsecureDevelopmentEndpoints: true, out var url, out var error);

        ok.Should().BeTrue();
        url!.ToString().TrimEnd('/').Should().Be(expected);
        error.Should().BeNull();
    }

    [Theory]
    [InlineData("http://localhost:8080")]
    [InlineData("http://192.168.1.20:8080")]
    public void Insecure_http_is_rejected_outside_development(string input)
    {
        var ok = CloudUrlPolicy.TryNormalize(input, allowInsecureDevelopmentEndpoints: false, out var url, out var error);

        ok.Should().BeFalse();
        url.Should().BeNull();
        error.Should().Contain("HTTPS");
    }

    [Theory]
    [InlineData("http://cloud.example.com")]
    [InlineData("http://8.8.8.8")]
    public void Insecure_http_is_rejected_for_public_hosts_even_in_development(string input)
    {
        var ok = CloudUrlPolicy.TryNormalize(input, allowInsecureDevelopmentEndpoints: true, out var url, out var error);

        ok.Should().BeFalse();
        url.Should().BeNull();
        error.Should().Contain("localhost");
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    public void Empty_values_are_rejected_with_a_clear_message(string? input)
    {
        var ok = CloudUrlPolicy.TryNormalize(input, allowInsecureDevelopmentEndpoints: true, out var url, out var error);

        ok.Should().BeFalse();
        url.Should().BeNull();
        error.Should().NotBeNullOrWhiteSpace();
    }

    [Theory]
    [InlineData("not a url")]
    [InlineData("cloud.example.com")]
    [InlineData("ftp://cloud.example.com")]
    [InlineData("https://user:secret@cloud.example.com")]
    [InlineData("https://cloud.example.com/api")]
    [InlineData("https://cloud.example.com/api/")]
    [InlineData("https://cloud.example.com?x=1")]
    [InlineData("https://cloud.example.com#fragment")]
    public void Malformed_or_unsupported_urls_are_rejected(string input)
    {
        var ok = CloudUrlPolicy.TryNormalize(input, allowInsecureDevelopmentEndpoints: true, out var url, out var error);

        ok.Should().BeFalse();
        url.Should().BeNull();
        error.Should().NotBeNullOrWhiteSpace();
    }
}
