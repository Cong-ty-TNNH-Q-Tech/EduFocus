namespace EduFocus.Ports.DTOs;

/// <summary>Preferences captured during onboarding; defaults are the safe values from AppSettings.</summary>
public sealed record OnboardingPreferences(bool MonitoringEnabled, bool StartWithWindows);

/// <summary>Persisted onboarding progress used to resume after a restart.</summary>
public sealed record OnboardingState(
    bool PrivacyConsentRecorded,
    bool CredentialConfigured,
    bool OnboardingCompleted,
    OnboardingPreferences Preferences);
