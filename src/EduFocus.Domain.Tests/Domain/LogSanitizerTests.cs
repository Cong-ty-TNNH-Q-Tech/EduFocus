using EduFocus.Domain.Privacy;
using FluentAssertions;

namespace EduFocus.Domain.Tests.Domain;

public class LogSanitizerTests
{
    [Theory]
    [InlineData("parent pin=123456 failed", "pin=***")]
    [InlineData("PIN: 4821 accepted", "PIN=***")]
    [InlineData("recovery code=ABCD-EFGH used", "recovery code=***")]
    [InlineData("recovery_code=ABCD-EFGH used", "recovery_code=***")]
    [InlineData("refresh_token=abc123 failed", "refresh_token=***")]
    [InlineData("api_key=sk-live-123", "api_key=***")]
    [InlineData("password = hunter2", "password=***")]
    public void Redact_RemovesSecretValue(string input, string expectedFragment)
    {
        var result = LogSanitizer.Redact(input);

        result.Should().Contain(expectedFragment);
        result.Should().NotContainAny("123456", "4821", "ABCD-EFGH", "abc123", "sk-live-123", "hunter2");
    }

    [Fact]
    public void Redact_RemovesAuthorizationHeaderValue()
    {
        var result = LogSanitizer.Redact("Authorization: Bearer abc.def.ghi");

        result.Should().Be("Authorization:***");
        result.Should().NotContain("abc.def.ghi");
    }

    [Fact]
    public void Redact_RemovesStandaloneBearerToken()
    {
        var result = LogSanitizer.Redact("request failed with Bearer eyJhbGciOi.payload.sig");

        result.Should().Be("request failed with Bearer ***");
    }

    [Fact]
    public void Redact_ReplacesFullUrlWithNormalizedDomain()
    {
        var result = LogSanitizer.Redact(
            "Blocked https://user:pw@Example.com:443/class/7?token=abc&q=private#section now.");

        result.Should().Be("Blocked [url:example.com] now.");
    }

    [Fact]
    public void Redact_KeepsInvalidUrlAsRedactedPlaceholder()
    {
        var result = LogSanitizer.Redact("see https:// not a link");

        result.Should().Be("see https:// not a link");
    }

    [Fact]
    public void Redact_CollapsesControlCharactersToSingleLine()
    {
        var result = LogSanitizer.Redact("line1\r\nline2\tpin=1234");

        result.Should().NotContain("\r").And.NotContain("\n").And.NotContain("\t");
        result.Should().Contain("line1 line2");
        result.Should().NotContain("1234");
    }

    [Fact]
    public void Redact_TruncatesLongMessages()
    {
        var result = LogSanitizer.Redact(new string('a', LogSanitizer.MaxLength + 100));

        result.Length.Should().BeGreaterThan(LogSanitizer.MaxLength);
        result.Should().EndWith("...[truncated]");
    }

    [Fact]
    public void RedactPreservingLines_KeepsStackTraceWhileRedacting()
    {
        const string text = "System.Exception: token=abc\n   at Foo.Bar() in C:\\app\\Foo.cs:line 1";

        var result = LogSanitizer.RedactPreservingLines(text);

        result.Should().Contain("System.Exception: token=***");
        result.Should().Contain("\n");
        result.Should().NotContain("token=abc");
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    public void Redact_ReturnsEmptyForBlankInput(string? input)
    {
        LogSanitizer.Redact(input).Should().BeEmpty();
        LogSanitizer.RedactPreservingLines(input).Should().BeEmpty();
    }
}
