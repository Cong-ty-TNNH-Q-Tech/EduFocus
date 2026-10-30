namespace EduFocus.Ports.Outbound.Services;

/// <summary>
/// Generates a high-entropy recovery code. The plaintext is returned once and
/// must never be persisted or logged; only its hash is stored.
/// </summary>
/// <summary>Creates a cryptographically random recovery code in canonical form.</summary>
public interface IRecoveryCodeGenerator
{
    string Generate();
}
