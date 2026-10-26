using EduFocus.Domain.Entities;
using EduFocus.Domain.Enums;
using EduFocus.Ports.Outbound.Services;
using Microsoft.EntityFrameworkCore;

namespace EduFocus.Infrastructure.Persistence;

public sealed class BlockedAttemptRecorder(AppDbContext db) : IBlockedAttemptRecorder
{
    public async Task RecordBlockedAppAsync(string processName, DateTimeOffset occurredAtUtc, CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(processName);
        if (occurredAtUtc.Offset != TimeSpan.Zero) throw new ArgumentException("Timestamp must be UTC.", nameof(occurredAtUtc));
        var sessionId = await db.StudySessions.AsNoTracking()
            .Where(session => session.Status == StudySessionStatus.Active)
            .Select(session => (Guid?)session.Id)
            .SingleOrDefaultAsync(cancellationToken);
        db.BlockedAttempts.Add(BlockedAttempt.Create(sessionId, occurredAtUtc, BlockTargetType.App,
            processName, BlockAttemptSource.ProcessWatcher, occurredAtUtc, processName: processName));
        await db.SaveChangesAsync(cancellationToken);
    }
}
