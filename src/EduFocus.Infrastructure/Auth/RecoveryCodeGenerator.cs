using System.Security.Cryptography;
using EduFocus.Domain.Security;
using EduFocus.Ports.Outbound.Services;

namespace EduFocus.Infrastructure.Auth;

/// <summary>Uniformly random 12-character recovery code (system architecture §9.1).</summary>
public sealed class RecoveryCodeGenerator : IRecoveryCodeGenerator
{
    public string Generate()
    {
        var characters = new char[ParentRecoveryCode.Length];
        for (var index = 0; index < characters.Length; index++)
        {
            characters[index] = ParentRecoveryCode.Alphabet[
                RandomNumberGenerator.GetInt32(ParentRecoveryCode.Alphabet.Length)];
        }

        return new string(characters);
    }
}
