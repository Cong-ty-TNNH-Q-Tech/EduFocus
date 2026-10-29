using EduFocus.Domain.Security;
using FluentAssertions;

namespace EduFocus.Domain.Tests.Domain;

public class ParentPinPolicyTests
{
    [Theory]
    [InlineData("482915")]
    [InlineData("135790")]
    [InlineData("90417283")]
    [InlineData("112233")]
    public void IsValid_AcceptsSixToEightDigits(string pin)
    {
        ParentPinPolicy.IsValid(pin).Should().BeTrue();
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("12345")]
    [InlineData("123456789")]
    [InlineData("12a456")]
    [InlineData("12 456")]
    [InlineData("000000")]
    [InlineData("77777777")]
    [InlineData("123456")]
    [InlineData("1234567")]
    [InlineData("987654")]
    [InlineData("98765432")]
    public void IsValid_RejectsShortLongNonDigitAndTrivialPins(string? pin)
    {
        ParentPinPolicy.IsValid(pin).Should().BeFalse();
    }

    [Fact]
    public void EnsureValid_ThrowsForWeakPin()
    {
        var act = () => ParentPinPolicy.EnsureValid("111111");

        act.Should().Throw<ArgumentException>().WithParameterName("pin");
    }

    [Fact]
    public void EnsureValid_AcceptsStrongPin()
    {
        var act = () => ParentPinPolicy.EnsureValid("482915");

        act.Should().NotThrow();
    }
}
