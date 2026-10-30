using EduFocus.Domain.Entities;
using EduFocus.Domain.Enums;
using EduFocus.Ports.DTOs;
using EduFocus.Ports.Inbound;
using EduFocus.Ports.Outbound.Persistence;
using EduFocus.Ports.Outbound.Services;

namespace EduFocus.UseCases.Onboarding;

/// <summary>
/// First-run wizard coordination from UC01. Consent, preferences, the PIN and
/// completion are persisted step by step so a restart resumes without losing
/// completed work. Onboarding cannot complete without consent and a parent PIN.
/// </summary>
public sealed class OnboardingService : IOnboardingUseCase
{
    private const string ConsentRequired = "Privacy consent must be recorded before the parent PIN is configured.";
    private const string CredentialRequired = "A parent PIN must be configured before onboarding can complete.";
    private const string SettingsMissing = "Onboarding settings were not saved.";

    private readonly IAppSettingsRepository _settings;
    private readonly IParentAuthUseCase _auth;
    private readonly IAuditLogRepository _auditLogs;
    private readonly ICorrelationContext _correlation;
    private readonly TimeProvider _timeProvider;

    public OnboardingService(
        IAppSettingsRepository settings,
        IParentAuthUseCase auth,
        IAuditLogRepository auditLogs,
        ICorrelationContext correlation,
        TimeProvider timeProvider)
    {
        _settings = settings ?? throw new ArgumentNullException(nameof(settings));
        _auth = auth ?? throw new ArgumentNullException(nameof(auth));
        _auditLogs = auditLogs ?? throw new ArgumentNullException(nameof(auditLogs));
        _correlation = correlation ?? throw new ArgumentNullException(nameof(correlation));
        _timeProvider = timeProvider ?? throw new ArgumentNullException(nameof(timeProvider));
    }

    public async Task<OnboardingState> GetStateAsync(CancellationToken cancellationToken = default)
    {
        var settings = await _settings.GetAsync(cancellationToken);
        var configured = await _auth.IsConfiguredAsync(cancellationToken);

        return new OnboardingState(
            settings?.HasPrivacyConsent == true,
            configured,
            settings?.OnboardingCompleted == true,
            new OnboardingPreferences(
                settings?.MonitoringEnabled ?? true,
                settings?.StartWithWindows ?? true));
    }

    public async Task RecordPrivacyConsentAsync(CancellationToken cancellationToken = default)
    {
        var now = UtcNow();
        var settings = await GetOrCreateAsync(now, cancellationToken);

        if (settings.HasPrivacyConsent)
            return;

        settings.RecordPrivacyConsent(now);
        await _settings.UpdateAsync(settings, cancellationToken);
        await WriteAuditAsync(
            AuditEventType.PrivacyConsentRecorded,
            "Parent recorded privacy consent and confirmed the student was informed.",
            now,
            cancellationToken);
    }

    public async Task SavePreferencesAsync(
        OnboardingPreferences preferences,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(preferences);

        var now = UtcNow();
        var settings = await GetOrCreateAsync(now, cancellationToken);

        settings.UpdateMonitoring(preferences.MonitoringEnabled, settings.MonitorOnlyDuringClasses, now);
        settings.UpdateStartWithWindows(preferences.StartWithWindows, now);

        await _settings.UpdateAsync(settings, cancellationToken);
        await WriteAuditAsync(
            AuditEventType.SettingsChanged,
            "Parent saved onboarding preferences.",
            now,
            cancellationToken);
    }

    public async Task<ParentCredentialSetupResult> ConfigurePinAsync(
        string pin,
        CancellationToken cancellationToken = default)
    {
        var settings = await _settings.GetAsync(cancellationToken);
        if (settings?.HasPrivacyConsent != true)
            throw new InvalidOperationException(ConsentRequired);

        return await _auth.SetupAsync(pin, cancellationToken);
    }

    public async Task<ParentRecoveryCodeResult> ReissueRecoveryCodeAsync(
        string pin,
        CancellationToken cancellationToken = default)
    {
        var settings = await _settings.GetAsync(cancellationToken);
        if (settings?.HasPrivacyConsent != true)
            throw new InvalidOperationException(ConsentRequired);

        return await _auth.RegenerateRecoveryCodeAsync(pin, cancellationToken);
    }

    public async Task CompleteAsync(CancellationToken cancellationToken = default)
    {
        var now = UtcNow();
        var settings = await _settings.GetAsync(cancellationToken)
            ?? throw new InvalidOperationException(SettingsMissing);

        if (settings.OnboardingCompleted)
            return;

        if (!settings.HasPrivacyConsent)
            throw new InvalidOperationException(ConsentRequired);

        if (!await _auth.IsConfiguredAsync(cancellationToken))
            throw new InvalidOperationException(CredentialRequired);

        settings.CompleteOnboarding(now);
        await _settings.UpdateAsync(settings, cancellationToken);
        await WriteAuditAsync(
            AuditEventType.OnboardingCompleted,
            "Parent completed first-run onboarding.",
            now,
            cancellationToken);
    }

    private async Task<AppSettings> GetOrCreateAsync(DateTime nowUtc, CancellationToken cancellationToken)
    {
        var existing = await _settings.GetAsync(cancellationToken);
        if (existing is not null)
            return existing;

        var settings = AppSettings.CreateDefault(nowUtc);
        await _settings.AddAsync(settings, cancellationToken);
        return settings;
    }

    private async Task WriteAuditAsync(
        AuditEventType eventType,
        string description,
        DateTime now,
        CancellationToken cancellationToken)
    {
        var auditLog = AuditLog.Record(
            AuditActor.Parent,
            eventType,
            description,
            now,
            metadataJson: null,
            _correlation.Current);

        await _auditLogs.AddAsync(auditLog, cancellationToken);
    }

    private DateTime UtcNow() => _timeProvider.GetUtcNow().UtcDateTime;
}
