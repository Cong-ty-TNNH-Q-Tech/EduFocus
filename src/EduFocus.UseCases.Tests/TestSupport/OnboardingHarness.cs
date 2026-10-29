using EduFocus.UseCases.Onboarding;

namespace EduFocus.UseCases.Tests.TestSupport;

internal sealed class OnboardingHarness
{
    public OnboardingHarness()
    {
        Service = CreateService();
    }

    public AuthHarness Auth { get; } = new();

    public FakeAppSettingsRepository Settings { get; } = new();

    public OnboardingService Service { get; }

    /// <summary>A new service over the same persisted stores simulates an app restart.</summary>
    public OnboardingService CreateService() =>
        new(Settings, Auth.UseCase, Auth.Audits, Auth.Correlation, Auth.Clock);
}
