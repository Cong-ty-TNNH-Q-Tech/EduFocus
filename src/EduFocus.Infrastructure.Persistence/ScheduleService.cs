using EduFocus.Domain.Entities;
using EduFocus.Domain.Enums;
using Microsoft.EntityFrameworkCore;

namespace EduFocus.Infrastructure.Persistence;

public sealed record ScheduleEvaluation(bool IsScheduled, bool IsOverridden, DateTimeOffset? OverrideUntilUtc, string? ActiveLabel);

/// <summary>Validates and persists local weekly protection windows.</summary>
public sealed class ScheduleService(AppDbContext db)
{
    private static readonly SemaphoreSlim MutationGate = new(1, 1);
    public async Task<IReadOnlyList<ScheduleWindow>> GetWindowsAsync(CancellationToken cancellationToken = default) =>
        await db.ScheduleWindows.AsNoTracking().OrderBy(x => x.DaysMask).ThenBy(x => x.StartMinutes).ToListAsync(cancellationToken);

    public async Task<ScheduleWindow> AddAsync(int daysMask, int startMinutes, int endMinutes, string? label,
        bool enabled, ParentModeSession parent, DateTimeOffset now, CancellationToken cancellationToken = default)
    {
        EnsureParent(parent, now);
        var window = ScheduleWindow.Create(daysMask, startMinutes, endMinutes, now, label, enabled);
        await MutationGate.WaitAsync(cancellationToken);
        try
        {
            ScheduleWindow.EnsureNoOverlap((await db.ScheduleWindows.ToListAsync(cancellationToken)).Append(window));
            await using var transaction = await db.Database.BeginTransactionAsync(cancellationToken);
            db.ScheduleWindows.Add(window);
            db.AuditLogs.Add(AuditLog.Create(now, AuditActor.Parent, "ScheduleWindowAdded", "A protection schedule window was added.", now));
            await db.SaveChangesAsync(cancellationToken);
            await transaction.CommitAsync(cancellationToken);
            return window;
        }
        finally { MutationGate.Release(); }
    }

    public async Task UpdateAsync(Guid id, int daysMask, int startMinutes, int endMinutes, string? label,
        bool enabled, ParentModeSession parent, DateTimeOffset now, CancellationToken cancellationToken = default)
    {
        EnsureParent(parent, now);
        await MutationGate.WaitAsync(cancellationToken);
        try
        {
            var window = await db.ScheduleWindows.SingleOrDefaultAsync(x => x.Id == id, cancellationToken)
                ?? throw new KeyNotFoundException("Schedule window was not found.");
            var replacement = ScheduleWindow.Create(daysMask, startMinutes, endMinutes, now, label, enabled);
            ScheduleWindow.EnsureNoOverlap((await db.ScheduleWindows.Where(x => x.Id != id).ToListAsync(cancellationToken)).Append(replacement));
            await using var transaction = await db.Database.BeginTransactionAsync(cancellationToken);
            window.Update(daysMask, startMinutes, endMinutes, label, enabled, now);
            db.AuditLogs.Add(AuditLog.Create(now, AuditActor.Parent, "ScheduleWindowUpdated", "A protection schedule window was updated.", now));
            await db.SaveChangesAsync(cancellationToken);
            await transaction.CommitAsync(cancellationToken);
        }
        finally { MutationGate.Release(); }
    }

    public async Task DeleteAsync(Guid id, ParentModeSession parent, DateTimeOffset now, CancellationToken cancellationToken = default)
    {
        EnsureParent(parent, now);
        await MutationGate.WaitAsync(cancellationToken);
        try
        {
            var window = await db.ScheduleWindows.SingleOrDefaultAsync(x => x.Id == id, cancellationToken)
                ?? throw new KeyNotFoundException("Schedule window was not found.");
            await using var transaction = await db.Database.BeginTransactionAsync(cancellationToken);
            db.ScheduleWindows.Remove(window);
            db.AuditLogs.Add(AuditLog.Create(now, AuditActor.Parent, "ScheduleWindowDeleted", "A protection schedule window was deleted.", now));
            await db.SaveChangesAsync(cancellationToken);
            await transaction.CommitAsync(cancellationToken);
        }
        finally { MutationGate.Release(); }
    }

    public async Task<ScheduleEvaluation> EvaluateAsync(DateTimeOffset now, TimeZoneInfo? timeZone = null,
        CancellationToken cancellationToken = default)
    {
        RequireUtc(now);
        timeZone ??= TimeZoneInfo.Local;
        var settings = await db.AppSettings.AsNoTracking().SingleOrDefaultAsync(cancellationToken);
        var overrideUntil = settings?.ScheduleOverrideUntilUtc;
        var overridden = overrideUntil is { } until && now < until;
        var local = TimeZoneInfo.ConvertTime(now, timeZone).DateTime;
        var active = await db.ScheduleWindows.AsNoTracking().Where(x => x.IsEnabled).ToListAsync(cancellationToken);
        var window = active.FirstOrDefault(x => x.AppliesAt(local));
        return new(window is not null, overridden, overridden ? overrideUntil : null, window?.Label);
    }

    public async Task<DateTimeOffset?> OverrideCurrentWindowAsync(ParentModeSession parent, DateTimeOffset now,
        TimeZoneInfo? timeZone = null, CancellationToken cancellationToken = default)
    {
        EnsureParent(parent, now);
        timeZone ??= TimeZoneInfo.Local;
        var local = TimeZoneInfo.ConvertTime(now, timeZone).DateTime;
        var windows = await db.ScheduleWindows.Where(x => x.IsEnabled).ToListAsync(cancellationToken);
        var window = windows.FirstOrDefault(x => x.AppliesAt(local));
        if (window is null) return null;
        var endLocal = local.Date.AddMinutes(window.EndMinutes);
        // If a window ends in the spring-forward gap, resume at the first valid local minute.
        while (timeZone.IsInvalidTime(endLocal)) endLocal = endLocal.AddMinutes(1);
        var endUtc = timeZone.IsAmbiguousTime(endLocal)
            ? timeZone.GetAmbiguousTimeOffsets(endLocal).Select(offset => new DateTimeOffset(endLocal, offset).ToUniversalTime()).Max()
            : new DateTimeOffset(TimeZoneInfo.ConvertTimeToUtc(endLocal, timeZone), TimeSpan.Zero);
        if (endUtc <= now) return null;
        var settings = await db.AppSettings.SingleOrDefaultAsync(cancellationToken)
            ?? throw new InvalidOperationException("Application settings are not initialized.");
        settings.SetScheduleOverride(endUtc, now);
        db.AuditLogs.Add(AuditLog.Create(now, AuditActor.Parent, "ScheduleOverride", "Protection was paused until the active schedule window ends.", now));
        await db.SaveChangesAsync(cancellationToken);
        return endUtc;
    }

    /// <summary>Maps Monday to bit 0 through Sunday to bit 6; windows use [start,end) local minutes.</summary>
    public static bool IsScheduledAt(IEnumerable<ScheduleWindow> windows, DateTime localTime) =>
        windows.Any(window => window.AppliesAt(localTime));

    private static void EnsureParent(ParentModeSession parent, DateTimeOffset now)
    {
        ArgumentNullException.ThrowIfNull(parent);
        RequireUtc(now);
        if (!parent.IsActive(now)) throw new UnauthorizedAccessException("An active Parent Mode session is required.");
    }

    private static void RequireUtc(DateTimeOffset now)
    {
        if (now.Offset != TimeSpan.Zero) throw new ArgumentException("Timestamp must be UTC.", nameof(now));
    }
}
