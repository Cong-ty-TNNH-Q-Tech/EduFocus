namespace EduFocus.Ports.Security;

/// <summary>
/// Raised when protection state is queried or mutated before initial onboarding settings exist.
/// </summary>
public sealed class OnboardingIncompleteException : InvalidOperationException
{
    public OnboardingIncompleteException(string message)
        : base(message)
    {
    }
}
