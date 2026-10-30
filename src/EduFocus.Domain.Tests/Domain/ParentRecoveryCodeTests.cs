using EduFocus.Domain.Security;
using FluentAssertions;

namespace EduFocus.Domain.Tests.Domain;

public class ParentRecoveryCodeTests
{
    [Theory]
    [InlineData("ABCDEFGHJKLM", "ABCDEFGHJKLM")]
    [InlineData("abcdefghjklm", "ABCDEFGHJKLM")]
    [InlineData("ABCD-EFGH-JKLM", "ABCDEFGHJKLM")]
    [InlineData("  abcd efgh jklm  ", "ABCDEFGHJKLM")]
    public void Normalize_RemovesSeparatorsAndUppercases(string input, string expected)
    {
        ParentRecoveryCode.Normalize(input).Should().Be(expected);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    public void Normalize_ReturnsEmptyForMissingInput(string? input)
    {
        ParentRecoveryCode.Normalize(input).Should().BeEmpty();
    }

    [Theory]
    [InlineData("ABCDEFGHJKLM")]
    [InlineData("abcdefghjklm")]
    [InlineData("NPQR-2345-6789")]
    public void IsValid_AcceptsCanonicalCodes(string code)
    {
        ParentRecoveryCode.IsValid(code).Should().BeTrue();
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("ABCDEFGHJKL")]
    [InlineData("ABCDEFGHJKLMN")]
    [InlineData("ABCDEFGHJKLI")]
    [InlineData("ABCDEFGHJKL0")]
    [InlineData("ABCDEFGHJKL!")]
    public void IsValid_RejectsWrongLengthOrAmbiguousCharacters(string? code)
    {
        ParentRecoveryCode.IsValid(code).Should().BeFalse();
    }

    [Fact]
    public void Alphabet_HasThirtyTwoUnambiguousCharacters()
    {
        ParentRecoveryCode.Alphabet.Should().HaveLength(32);
        ParentRecoveryCode.Alphabet.Distinct().Should().HaveCount(32);
        ParentRecoveryCode.Alphabet.Should().NotContainAny("I", "O", "0", "1");
    }
}
