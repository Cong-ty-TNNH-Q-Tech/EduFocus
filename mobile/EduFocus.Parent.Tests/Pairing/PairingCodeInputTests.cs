using EduFocus.Parent.Core.Pairing;

namespace EduFocus.Parent.Tests.Pairing;

public sealed class PairingCodeInputTests
{
    [Theory]
    [InlineData("ABCD2345", "ABCD2345")]
    [InlineData("abcd2345", "ABCD2345")]
    [InlineData("abcd-2345", "ABCD2345")]
    [InlineData("  ABCD 2345  ", "ABCD2345")]
    public void Normalizes_typed_codes(string input, string expected)
    {
        Assert.True(PairingCodeInput.TryNormalize(input, out var normalized));
        Assert.Equal(expected, normalized);
    }

    [Fact]
    public void Folds_confusable_letters()
    {
        Assert.True(PairingCodeInput.TryNormalize("ILO0-1234", out var normalized));
        Assert.Equal("11001234", normalized);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("ABCD234")]
    [InlineData("ABCD23456")]
    [InlineData("ABCD23U5")]
    public void Rejects_invalid_codes(string? input)
    {
        Assert.False(PairingCodeInput.TryNormalize(input, out var normalized));
        Assert.Equal(string.Empty, normalized);
    }
}
