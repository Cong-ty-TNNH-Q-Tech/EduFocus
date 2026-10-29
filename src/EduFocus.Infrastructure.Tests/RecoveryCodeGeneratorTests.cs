using EduFocus.Domain.Security;
using EduFocus.Infrastructure.Auth;
using FluentAssertions;

namespace EduFocus.Infrastructure.Tests;

public sealed class RecoveryCodeGeneratorTests
{
    [Fact]
    public void Generate_returns_canonical_twelve_character_codes()
    {
        var generator = new RecoveryCodeGenerator();

        var codes = Enumerable.Range(0, 50).Select(_ => generator.Generate()).ToList();

        codes.Should().OnlyContain(code => ParentRecoveryCode.IsValid(code));
        codes.Should().OnlyHaveUniqueItems();
    }
}
