using EduFocus.Domain.Entities;
using EduFocus.Domain.Enums;
using Microsoft.EntityFrameworkCore;

namespace EduFocus.Infrastructure.Persistence;

public sealed record OnboardingProgress(
    bool PrivacyAccepted,
    bool CredentialsConfigured,
    bool Completed,
    bool PreferencesSaved,
    bool MonitoringEnabled,
    int? CameraIndex,
    bool StartWithWindows);

/// <summary>Persists resumable onboarding milestones without retaining PIN or recovery plaintext.</summary>
public sealed class OnboardingService(AppDbContext db)
{
    public async Task<OnboardingProgress> GetProgressAsync(CancellationToken cancellationToken = default)
    {
        var settings = await db.AppSettings.AsNoTracking().SingleOrDefaultAsync(cancellationToken);
        var accepted = await db.AuditLogs.AsNoTracking().AnyAsync(x => x.EventType == "PrivacyNoticeAccepted", cancellationToken);
        var credentials = await db.ParentCredentials.AsNoTracking().AnyAsync(cancellationToken);
        int? cameraIndex = int.TryParse(settings?.CameraDeviceId, out var index) ? index : null;
        return new(accepted, credentials, settings?.OnboardingCompleted == true, settings is not null,
            settings?.MonitoringEnabled == true, cameraIndex, settings?.StartWithWindows == true);
    }

    public async Task AcceptPrivacyNoticeAsync(DateTimeOffset now, CancellationToken cancellationToken = default)
    {
        now = RequireUtc(now);
        if (await db.AuditLogs.AnyAsync(x => x.EventType == "PrivacyNoticeAccepted", cancellationToken)) return;
        db.AuditLogs.Add(AuditLog.Create(now, AuditActor.Parent, "PrivacyNoticeAccepted", "Parent accepted the local privacy notice.", now));
        await db.SaveChangesAsync(cancellationToken);
    }

    public async Task SavePreferencesAsync(bool monitoringEnabled, int? cameraIndex, bool startWithWindows, DateTimeOffset now, CancellationToken cancellationToken = default)
    {
        now = RequireUtc(now);
        if (monitoringEnabled && cameraIndex is null) throw new ArgumentException("Select and check a camera before enabling monitoring.", nameof(cameraIndex));
        if (cameraIndex is < 0 or > 15) throw new ArgumentOutOfRangeException(nameof(cameraIndex));
        if (!await db.AuditLogs.AnyAsync(x => x.EventType == "PrivacyNoticeAccepted", cancellationToken))
            throw new InvalidOperationException("The privacy notice must be accepted before saving onboarding preferences.");
        if (!await db.ParentCredentials.AnyAsync(cancellationToken))
            throw new InvalidOperationException("A parent PIN must be configured before saving onboarding preferences.");

        await using var transaction = await db.Database.BeginTransactionAsync(cancellationToken);
        var settings = await db.AppSettings.SingleOrDefaultAsync(cancellationToken);
        if (settings is null)
        {
            settings = AppSettings.Create(now);
            db.AppSettings.Add(settings);
        }
        settings.Configure(monitoringEnabled, false, false, cameraIndex?.ToString(System.Globalization.CultureInfo.InvariantCulture),
            settings.DistractionThresholdSeconds, settings.AbsenceThresholdSeconds, settings.SampleIntervalSeconds,
            settings.ActivityRetentionDays, settings.EventRetentionDays, settings.SessionRetentionDays,
            startWithWindows, settings.Theme, settings.Language, false, now);
        await db.SaveChangesAsync(cancellationToken);
        await transaction.CommitAsync(cancellationToken);
    }

    public async Task CompleteAsync(DateTimeOffset now, CancellationToken cancellationToken = default)
    {
        now = RequireUtc(now);
        await using var transaction = await db.Database.BeginTransactionAsync(cancellationToken);
        if (!await db.AuditLogs.AnyAsync(x => x.EventType == "PrivacyNoticeAccepted", cancellationToken))
            throw new InvalidOperationException("The privacy notice must be accepted before onboarding can finish.");
        if (!await db.ParentCredentials.AnyAsync(cancellationToken))
            throw new InvalidOperationException("A parent PIN must be configured before onboarding can finish.");
        var settings = await db.AppSettings.SingleOrDefaultAsync(cancellationToken)
            ?? throw new InvalidOperationException("Onboarding preferences must be saved before onboarding can finish.");
        if (settings.MonitoringEnabled && !int.TryParse(settings.CameraDeviceId, out _))
            throw new InvalidOperationException("A camera must be selected before enabling monitoring.");
        settings.CompleteOnboarding(now);
        db.AuditLogs.Add(AuditLog.Create(now, AuditActor.Parent, "OnboardingCompleted", "Required first-run steps were completed.", now));
        await db.SaveChangesAsync(cancellationToken);
        await transaction.CommitAsync(cancellationToken);
    }

    private static DateTimeOffset RequireUtc(DateTimeOffset now) => now.Offset == TimeSpan.Zero ? now : throw new ArgumentException("Timestamp must be UTC.", nameof(now));
}
