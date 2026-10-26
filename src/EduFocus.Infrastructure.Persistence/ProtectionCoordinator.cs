using EduFocus.Domain.Entities;
using EduFocus.Domain.Enums;
using EduFocus.Ports.Outbound.Services;
using Microsoft.EntityFrameworkCore;

namespace EduFocus.Infrastructure.Persistence;

public sealed record ProtectionStatus(ProtectionState State, Guid? ActiveSessionId, bool EngineAvailable);

/// <summary>Persists Protection state only after the enforcement engine has accepted the matching transition.</summary>
public sealed class ProtectionCoordinator(AppDbContext db, IProtectionEnginePort engine)
{
    private static readonly SemaphoreSlim ProcessGate = new(1, 1);
    private bool _restored;

    public async Task<ProtectionStatus> GetStatusAsync(CancellationToken cancellationToken = default)
    {
        var settings = await db.AppSettings.AsNoTracking().SingleOrDefaultAsync(cancellationToken);
        var active = await db.StudySessions.AsNoTracking().SingleOrDefaultAsync(x => x.Status == StudySessionStatus.Active, cancellationToken);
        return new(settings?.Protection ?? ProtectionState.Off, active?.Id, engine.IsAvailable);
    }

    public async Task<ProtectionStatus> SetProtectionAsync(bool enabled, ParentModeSession parentSession, DateTimeOffset now, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(parentSession);
        now = RequireUtc(now);
        if (!parentSession.IsActive(now)) throw new UnauthorizedAccessException("An active Parent Mode session is required.");
        await ProcessGate.WaitAsync(cancellationToken);
        try
        {
            var settings = await db.AppSettings.SingleOrDefaultAsync(cancellationToken)
                ?? throw new InvalidOperationException("Application settings are not initialized.");
            var rules = await db.BlockRules.AsNoTracking().Where(rule => rule.IsEnabled && rule.EnforcementStatus == RuleEnforcementStatus.Enforced && (rule.ExpiresAtUtc == null || rule.ExpiresAtUtc > now)).ToArrayAsync(cancellationToken);
            var desired = enabled ? ProtectionState.On : ProtectionState.Off;

            if (settings.Protection == desired)
            {
                await engine.ReconcileAsync(enabled, rules, cancellationToken);
                return await GetStatusAsync(cancellationToken);
            }

            if (enabled)
            {
                if (!engine.IsAvailable) throw new InvalidOperationException("The protection engine is unavailable; Protection remains OFF.");
                try
                {
                    await engine.StartAsync(rules, cancellationToken);
                    await using var transaction = await db.Database.BeginTransactionAsync(cancellationToken);
                    await StartStateAsync(settings, now, actor: AuditActor.Parent, trigger: StudySessionTrigger.Manual, cancellationToken);
                    await transaction.CommitAsync(cancellationToken);
                }
                catch
                {
                    try { await engine.ReconcileAsync(false, rules, cancellationToken); }
                    catch { /* Keep the original persistence error; startup reconciliation will retry. */ }
                    throw;
                }
            }
            else
            {
                if (!engine.IsAvailable) throw new InvalidOperationException("The protection engine is unavailable; Protection remains ON until resources can be safely removed.");
                try
                {
                    await engine.StopAsync(cancellationToken);
                    await using var transaction = await db.Database.BeginTransactionAsync(cancellationToken);
                    var active = await db.StudySessions.SingleOrDefaultAsync(x => x.Status == StudySessionStatus.Active, cancellationToken);
                    active?.Complete(now);
                    settings.SetProtection(ProtectionState.Off, now);
                    db.AuditLogs.Add(AuditLog.Create(now, AuditActor.Parent, "ProtectionOff", "Protection was disabled by the parent.", now));
                    await db.SaveChangesAsync(cancellationToken);
                    await transaction.CommitAsync(cancellationToken);
                }
                catch
                {
                    try { await engine.ReconcileAsync(true, rules, cancellationToken); }
                    catch { /* Persisted state remains ON; startup reconciliation will report engine failure. */ }
                    throw;
                }
            }
            return await GetStatusAsync(cancellationToken);
        }
        finally { ProcessGate.Release(); }
    }

    public async Task<ProtectionStatus> ReconcileAfterRestartAsync(DateTimeOffset now, CancellationToken cancellationToken = default)
    {
        now = RequireUtc(now);
        await ProcessGate.WaitAsync(cancellationToken);
        try
        {
            var settings = await db.AppSettings.SingleOrDefaultAsync(cancellationToken);
            if (settings is null)
            {
                await engine.ReconcileAsync(false, Array.Empty<BlockRule>(), cancellationToken);
                _restored = true;
                return new(ProtectionState.Off, null, engine.IsAvailable);
            }

            var enabled = settings.Protection == ProtectionState.On;
            var rules = await db.BlockRules.AsNoTracking().Where(rule => rule.IsEnabled && rule.EnforcementStatus == RuleEnforcementStatus.Enforced && (rule.ExpiresAtUtc == null || rule.ExpiresAtUtc > now)).ToArrayAsync(cancellationToken);
            if (!_restored) await engine.ReconcileAsync(enabled, rules, cancellationToken);

            if (!_restored)
            {
                await using var transaction = await db.Database.BeginTransactionAsync(cancellationToken);
                var active = await db.StudySessions.SingleOrDefaultAsync(x => x.Status == StudySessionStatus.Active, cancellationToken);
                if (!enabled && active is not null)
                {
                    active.InterruptAtLastHeartbeat();
                    db.AuditLogs.Add(AuditLog.Create(now, AuditActor.System, "SessionInterrupted", "An active session was interrupted while Protection was OFF.", now));
                    await db.SaveChangesAsync(cancellationToken);
                }
                else if (enabled && active is not null)
                {
                    active.InterruptAtLastHeartbeat();
                    db.AuditLogs.Add(AuditLog.Create(now, AuditActor.System, "SessionInterrupted", "The previous session ended at its last heartbeat during restart.", now));
                    await db.SaveChangesAsync(cancellationToken);
                    active = null;
                }

                if (enabled && active is null)
                {
                    var recovered = StudySession.Start(null, StudySessionTrigger.Recovery, settings.MonitoringEnabled,
                        DateOnly.FromDateTime(now.LocalDateTime), now);
                    db.StudySessions.Add(recovered);
                    db.AuditLogs.Add(AuditLog.Create(now, AuditActor.System, "ProtectionRecovered", "Protection resources were reconciled after restart.", now));
                    await db.SaveChangesAsync(cancellationToken);
                }
                await transaction.CommitAsync(cancellationToken);
                _restored = true;
            }
            return await GetStatusAsync(cancellationToken);
        }
        finally { ProcessGate.Release(); }
    }

    private async Task StartStateAsync(AppSettings settings, DateTimeOffset now, AuditActor actor, StudySessionTrigger trigger, CancellationToken cancellationToken)
    {
        var active = await db.StudySessions.SingleOrDefaultAsync(x => x.Status == StudySessionStatus.Active, cancellationToken);
        if (active is not null)
        {
            active.InterruptAtLastHeartbeat();
            await db.SaveChangesAsync(cancellationToken);
        }
        settings.SetProtection(ProtectionState.On, now);
        if (trigger == StudySessionTrigger.Manual) settings.SetScheduleOverride(null, now);
        var session = StudySession.Start(null, trigger, settings.MonitoringEnabled, DateOnly.FromDateTime(now.LocalDateTime), now);
        db.StudySessions.Add(session);
        db.AuditLogs.Add(AuditLog.Create(now, actor, "ProtectionOn", "Protection was enabled.", now));
        await db.SaveChangesAsync(cancellationToken);
    }

    private static DateTimeOffset RequireUtc(DateTimeOffset now) => now.Offset == TimeSpan.Zero ? now : throw new ArgumentException("Timestamp must be UTC.", nameof(now));
}
