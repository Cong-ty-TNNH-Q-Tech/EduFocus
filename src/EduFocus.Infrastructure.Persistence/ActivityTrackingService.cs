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
    private bool _idleRecorded;

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

            using var scope = scopeFactory.CreateScope();
            var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
            var session = await db.StudySessions.SingleOrDefaultAsync(x => x.Status == StudySessionStatus.Active, cancellationToken);
            if (session is null) return;

            if (snapshot.IdleSeconds > 300)
            {
                if (!_idleRecorded)
                {
                    var duration = GetDuration(now);
                    var idleSample = ActivitySample.Create(session.Id, now, duration, now,
                        ActivityCategory.Neutral, ActivityUrlSource.None, processName: snapshot.ProcessName,
                        appName: snapshot.AppName, isForeground: false, idleSeconds: snapshot.IdleSeconds);
                    db.ActivitySamples.Add(idleSample);
                    session.AddIdleSeconds(duration, now);
                    await db.SaveChangesAsync(cancellationToken);
                    _lastSampleUtc = now;
                    _idleRecorded = true;
                }
                return;
            }

            _idleRecorded = false;
            if (!string.Equals(snapshot.ProcessName, _current?.ProcessName, StringComparison.OrdinalIgnoreCase))
            {
                if (!string.Equals(snapshot.ProcessName, _candidate?.ProcessName, StringComparison.OrdinalIgnoreCase))
                {
                    _candidate = snapshot;
                    _candidateSinceUtc = now;
                    return;
                }
                if (now - _candidateSinceUtc < ForegroundDebounce) return;
                _current = _candidate;
                _candidate = null;
            }
            else
            {
                _current = snapshot;
                _candidate = null;
            }

            var stable = _current ?? snapshot;
            var category = ClassifyProcess(stable.ProcessName);
            var elapsed = GetDuration(now);
            var sample = ActivitySample.Create(session.Id, now, elapsed, now, category,
                stable.Url is null ? ActivityUrlSource.None : stable.UrlSource,
                stable.AppName, stable.ProcessName, stable.WindowTitle, stable.Url, idleSeconds: snapshot.IdleSeconds);
            if (sample.Category != category) throw new InvalidOperationException("Activity sample category changed during construction.");
            db.ActivitySamples.Add(sample);
            session.AddActivity(category, elapsed, onlineClass: false, now);
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
            name.Equals("teams", StringComparison.OrdinalIgnoreCase) || name.Equals("zoom", StringComparison.OrdinalIgnoreCase) ||
            name.Equals("notepad", StringComparison.OrdinalIgnoreCase)) return ActivityCategory.Study;
        if (name.Equals("steam", StringComparison.OrdinalIgnoreCase) || name.Equals("epicgameslauncher", StringComparison.OrdinalIgnoreCase) ||
            name.Equals("robloxplayerbeta", StringComparison.OrdinalIgnoreCase) || name.Equals("discord", StringComparison.OrdinalIgnoreCase) ||
            name.Equals("tiktok", StringComparison.OrdinalIgnoreCase) || name.Equals("instagram", StringComparison.OrdinalIgnoreCase)) return ActivityCategory.Distraction;
        if (name.Equals("explorer", StringComparison.OrdinalIgnoreCase) || name.Equals("settings", StringComparison.OrdinalIgnoreCase) ||
            name.Equals("searchhost", StringComparison.OrdinalIgnoreCase) || name.Equals("startmenuexperiencehost", StringComparison.OrdinalIgnoreCase)) return ActivityCategory.Neutral;
        return ActivityCategory.Unknown;
    }
}
