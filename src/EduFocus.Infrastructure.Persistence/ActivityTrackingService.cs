using EduFocus.Domain.Entities;
using EduFocus.Domain.Enums;
using EduFocus.Ports.Outbound.Services;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace EduFocus.Infrastructure.Persistence;

/// <summary>Samples minimized foreground metadata and persists one idle sample per idle period.</summary>
public sealed class ActivityTrackingService(
    IServiceScopeFactory scopeFactory,
    IForegroundActivityPort foreground,
    IAppLogger logger)
{
    private static readonly TimeSpan ForegroundDebounce = TimeSpan.FromSeconds(2);
    private readonly SemaphoreSlim _gate = new(1, 1);
    private ForegroundActivitySnapshot? _current;
    private ForegroundActivitySnapshot? _candidate;
    private DateTimeOffset _candidateSinceUtc;
    private DateTimeOffset? _lastSampleUtc;
    private DateTimeOffset? _noSessionRetryUtc;
    private Guid? _sessionId;
    private bool _idleRecorded;
    private static readonly TimeSpan SampleInterval = TimeSpan.FromSeconds(15);

    public async Task ScanOnceAsync(DateTimeOffset now, CancellationToken cancellationToken = default)
    {
        if (now.Offset != TimeSpan.Zero) throw new ArgumentException("Timestamp must be UTC.", nameof(now));
        await _gate.WaitAsync(cancellationToken);
        try
        {
            ForegroundActivitySnapshot snapshot;
            try { snapshot = foreground.Capture(); }
            catch (Exception ex)
            {
                logger.Warning("Foreground activity metadata is temporarily unavailable.");
                logger.Error("Foreground activity capture failed.", ex);
                return;
            }

            var isIdle = snapshot.IdleSeconds > 300;
            if (!isIdle) _idleRecorded = false;
            var stable = snapshot;
            if (!isIdle)
            {
                if (!SameForeground(snapshot, _current))
                {
                    if (!SameForeground(snapshot, _candidate))
                    {
                        _candidate = snapshot;
                        _candidateSinceUtc = now;
                        return;
                    }
                    if (now - _candidateSinceUtc < ForegroundDebounce) return;
                    _current = _candidate;
                    _candidate = null;
                }
                else { _current = snapshot; _candidate = null; }
                stable = _current ?? snapshot;
                if (_lastSampleUtc is { } sampledAt && now - sampledAt < SampleInterval) return;
            }
            else if (_idleRecorded) return;

            if (_noSessionRetryUtc is { } retryAt && now < retryAt) return;
            using var scope = scopeFactory.CreateScope();
            var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
            var session = await db.StudySessions.SingleOrDefaultAsync(x => x.Status == StudySessionStatus.Active, cancellationToken);
            if (session is null)
            {
                _sessionId = null;
                _lastSampleUtc = null;
                _idleRecorded = false;
                _noSessionRetryUtc = now + SampleInterval;
                return;
            }
            _noSessionRetryUtc = null;
            if (_sessionId != session.Id)
            {
                _sessionId = session.Id;
                _lastSampleUtc = now;
                _idleRecorded = false;
            }
            var monitorOnlyDuringClasses = await db.AppSettings.AsNoTracking()
                .Select(settings => settings.MonitorOnlyDuringClasses).SingleOrDefaultAsync(cancellationToken);

            if (isIdle)
            {
                var duration = GetDuration(now);
                var idleOnlineClass = IsOnlineClass(snapshot);
                var idleSample = ActivitySample.Create(session.Id, now, duration, now,
                    idleOnlineClass ? ActivityCategory.Study : ActivityCategory.Neutral, ActivityUrlSource.None,
                    appName: snapshot.AppName, processName: snapshot.ProcessName, windowTitle: snapshot.WindowTitle,
                    isForeground: false, isOnlineClass: idleOnlineClass, idleSeconds: snapshot.IdleSeconds);
                db.ActivitySamples.Add(idleSample);
                session.AddIdleSeconds(duration, now);
                if (idleOnlineClass) session.AddActivity(ActivityCategory.Study, duration, true, now);
                if (session.MonitoringEnabled && (!monitorOnlyDuringClasses || idleOnlineClass))
                    session.AddMonitoringEligibleSeconds(duration, now);
                await db.SaveChangesAsync(cancellationToken);
                _lastSampleUtc = now;
                _idleRecorded = true;
                return;
            }

            var onlineClass = IsOnlineClass(stable);
            var category = onlineClass ? ActivityCategory.Study : ClassifyProcess(stable.ProcessName);
            var elapsed = GetDuration(now);
            var sample = ActivitySample.Create(session.Id, now, elapsed, now, category,
                stable.Url is null ? ActivityUrlSource.None : stable.UrlSource,
                stable.AppName, stable.ProcessName, stable.WindowTitle, stable.Url,
                isOnlineClass: onlineClass, idleSeconds: snapshot.IdleSeconds);
            if (sample.Category != category) throw new InvalidOperationException("Activity sample category changed during construction.");
            db.ActivitySamples.Add(sample);
            session.AddActivity(category, elapsed, onlineClass, now);
            if (session.MonitoringEnabled && (!monitorOnlyDuringClasses || onlineClass))
                session.AddMonitoringEligibleSeconds(elapsed, now);
            await db.SaveChangesAsync(cancellationToken);
            _lastSampleUtc = now;
        }
        finally { _gate.Release(); }
    }

    private long GetDuration(DateTimeOffset now)
    {
        if (_lastSampleUtc is not { } previous || now <= previous) return 0;
        return (long)Math.Min(30, Math.Floor((now - previous).TotalSeconds));
    }

    private static bool SameForeground(ForegroundActivitySnapshot left, ForegroundActivitySnapshot? right) =>
        right is not null && string.Equals(left.ProcessName, right.ProcessName, StringComparison.OrdinalIgnoreCase) &&
        string.Equals(left.WindowTitle, right.WindowTitle, StringComparison.Ordinal);

    public static ActivityCategory ClassifyProcess(string? processName)
    {
        var name = (processName ?? string.Empty).Trim();
        var separator = Math.Max(name.LastIndexOf('/'), name.LastIndexOf('\\'));
        if (separator >= 0) name = name[(separator + 1)..];
        if (name.EndsWith(".exe", StringComparison.OrdinalIgnoreCase)) name = name[..^4];
        name = new string(name.Where(char.IsLetterOrDigit).ToArray());
        if (name.Length == 0) return ActivityCategory.Unknown;
        if (name.Equals("winword", StringComparison.OrdinalIgnoreCase) || name.Equals("excel", StringComparison.OrdinalIgnoreCase) ||
            name.Equals("powerpnt", StringComparison.OrdinalIgnoreCase) || name.Equals("onenote", StringComparison.OrdinalIgnoreCase) ||
            name.Equals("code", StringComparison.OrdinalIgnoreCase) || name.Equals("devenv", StringComparison.OrdinalIgnoreCase) ||
            name.Equals("notepad", StringComparison.OrdinalIgnoreCase)) return ActivityCategory.Study;
        if (name.Equals("steam", StringComparison.OrdinalIgnoreCase) || name.Equals("epicgameslauncher", StringComparison.OrdinalIgnoreCase) ||
            name.Equals("robloxplayerbeta", StringComparison.OrdinalIgnoreCase) || name.Equals("discord", StringComparison.OrdinalIgnoreCase) ||
            name.Equals("tiktok", StringComparison.OrdinalIgnoreCase) || name.Equals("instagram", StringComparison.OrdinalIgnoreCase)) return ActivityCategory.Distraction;
        if (name.Equals("explorer", StringComparison.OrdinalIgnoreCase) || name.Equals("settings", StringComparison.OrdinalIgnoreCase) ||
            name.Equals("searchhost", StringComparison.OrdinalIgnoreCase) || name.Equals("startmenuexperiencehost", StringComparison.OrdinalIgnoreCase)) return ActivityCategory.Neutral;
        return ActivityCategory.Unknown;
    }

    public static bool IsOnlineClass(ForegroundActivitySnapshot snapshot)
    {
        ArgumentNullException.ThrowIfNull(snapshot);
        var process = NormalizeProcessName(snapshot.ProcessName);
        var title = (snapshot.WindowTitle ?? string.Empty).ToLowerInvariant();
        var meetingTitle = title.Contains("meeting", StringComparison.Ordinal) || title.Contains("meet now", StringComparison.Ordinal) ||
            title.Contains("google meet", StringComparison.Ordinal) || title.Contains("meet.google.com", StringComparison.Ordinal) ||
            title.Contains("cuộc họp", StringComparison.Ordinal) || title.Contains("cuoc hop", StringComparison.Ordinal) ||
            title.Contains("reunión", StringComparison.Ordinal) || title.Contains("réunion", StringComparison.Ordinal) ||
            title.Contains("besprechung", StringComparison.Ordinal) || title.Contains("reunião", StringComparison.Ordinal);

        if (title.Contains("meet.google.com", StringComparison.Ordinal) || title.Contains("google meet", StringComparison.Ordinal)) return true;
        return meetingTitle && (process is "teams" or "msteams" or "zoom" or "zoomworkplace");
    }

    private static string NormalizeProcessName(string? processName)
    {
        var name = (processName ?? string.Empty).Trim();
        var separator = Math.Max(name.LastIndexOf('/'), name.LastIndexOf('\\'));
        if (separator >= 0) name = name[(separator + 1)..];
        if (name.EndsWith(".exe", StringComparison.OrdinalIgnoreCase)) name = name[..^4];
        return new string(name.Where(char.IsLetterOrDigit).ToArray()).ToLowerInvariant();
    }
}
