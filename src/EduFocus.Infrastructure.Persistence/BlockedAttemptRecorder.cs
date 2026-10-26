using EduFocus.Domain.Entities;
using EduFocus.Domain.Enums;
using EduFocus.Ports.Outbound.Services;
using Microsoft.EntityFrameworkCore;

namespace EduFocus.Infrastructure.Persistence;

public sealed class BlockedAttemptRecorder(AppDbContext db) : IBlockedAttemptRecorder
{
    private static readonly SemaphoreSlim Gate = new(1, 1);
    private static readonly Dictionary<string, DateTimeOffset> LastRecorded = new(StringComparer.OrdinalIgnoreCase);

    public async Task RecordBlockedAppAsync(string processName, DateTimeOffset occurredAtUtc, CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(processName);
        await RecordAsync(BlockTargetType.App, processName, processName, BlockAttemptSource.ProcessWatcher, occurredAtUtc, cancellationToken);
    }

    public async Task RecordBlockedDomainAsync(string domain, DateTimeOffset occurredAtUtc, CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(domain);
        await RecordAsync(BlockTargetType.Domain, domain, null, BlockAttemptSource.BlockPage, occurredAtUtc, cancellationToken);
    }

    private async Task RecordAsync(BlockTargetType targetType, string target, string? processName, BlockAttemptSource source,
        DateTimeOffset occurredAtUtc, CancellationToken cancellationToken)
    {
        if (occurredAtUtc.Offset != TimeSpan.Zero) throw new ArgumentException("Timestamp must be UTC.", nameof(occurredAtUtc));
        var key = $"{source}:{targetType}:{target.Trim().ToLowerInvariant()}";
        await Gate.WaitAsync(cancellationToken);
        try
        {
            if (LastRecorded.TryGetValue(key, out var last) && occurredAtUtc - last < TimeSpan.FromSeconds(30)) return;
            var sessionId = await db.StudySessions.AsNoTracking()
                .Where(session => session.Status == StudySessionStatus.Active)
                .Select(session => (Guid?)session.Id)
                .SingleOrDefaultAsync(cancellationToken);
            db.BlockedAttempts.Add(BlockedAttempt.Create(sessionId, occurredAtUtc, targetType,
                target, source, occurredAtUtc, domain: targetType == BlockTargetType.Domain ? target : null, processName: processName));
            await db.SaveChangesAsync(cancellationToken);
            LastRecorded[key] = occurredAtUtc;
            foreach (var stale in LastRecorded.Where(x => occurredAtUtc - x.Value >= TimeSpan.FromMinutes(30)).Select(x => x.Key).ToArray())
                LastRecorded.Remove(stale);
        }
        finally { Gate.Release(); }
    }
}
