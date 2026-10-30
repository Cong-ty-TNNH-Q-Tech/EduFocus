using EduFocus.Domain.Entities;
using EduFocus.Domain.Enums;
using EduFocus.Domain.Reports;
using EduFocus.Ports.DTOs;
using EduFocus.Ports.Inbound;
using EduFocus.Ports.Outbound.Persistence;

namespace EduFocus.UseCases.Reports;

/// <summary>
/// Builds day/week reports from session summaries plus detail data (UC10,
/// issue #36). Sessions are grouped by their stored local start date, so DST
/// never moves a session between report days; the hourly timeline converts
/// stored UTC samples with the local time zone at read time.
/// </summary>
public sealed class ReportService : IReportUseCase
{
    public const int MaxDisplayedSessions = 50;
    public const int MaxReportSamples = 100_000;
    public const int MaxAttentionEvents = 100;
    public const int TopActivityLimit = 10;

    private readonly IStudySessionRepository _sessions;
    private readonly IActivitySampleRepository _samples;
    private readonly IAttentionEventRepository _attentionEvents;
    private readonly TimeProvider _timeProvider;

    public ReportService(
        IStudySessionRepository sessions,
        IActivitySampleRepository samples,
        IAttentionEventRepository attentionEvents,
        TimeProvider timeProvider)
    {
        _sessions = sessions ?? throw new ArgumentNullException(nameof(sessions));
        _samples = samples ?? throw new ArgumentNullException(nameof(samples));
        _attentionEvents = attentionEvents ?? throw new ArgumentNullException(nameof(attentionEvents));
        _timeProvider = timeProvider ?? throw new ArgumentNullException(nameof(timeProvider));
    }

    public async Task<StudyReport> GetAsync(
        ReportPeriod period,
        CancellationToken cancellationToken = default)
    {
        if (period.Start == default)
            throw new ArgumentException("A report period is required.", nameof(period));

        var sessions = await _sessions
            .ListByLocalDateRangeAsync(period.Start, period.End, cancellationToken)
            .ConfigureAwait(false);

        if (sessions.Count == 0)
            return StudyReport.Empty(period);

        var sessionIds = sessions.Select(session => session.Id).ToArray();

        // One extra row detects a cap without loading unbounded detail rows.
        var samples = await _samples
            .ListForReportAsync(sessionIds, MaxReportSamples + 1, cancellationToken)
            .ConfigureAwait(false);
        var timelineTruncated = samples.Count > MaxReportSamples;
        if (timelineTruncated)
            samples = samples.Take(MaxReportSamples).ToArray();

        var events = await _attentionEvents
            .ListForReportAsync(sessionIds, MaxAttentionEvents + 1, cancellationToken)
            .ConfigureAwait(false);
        var eventsTruncated = events.Count > MaxAttentionEvents;
        if (eventsTruncated)
            events = events.Take(MaxAttentionEvents).ToArray();

        var sampleSessionIds = samples.Select(sample => sample.SessionId).ToHashSet();
        var eventSessionIds = events.Select(attentionEvent => attentionEvent.SessionId).ToHashSet();

        return new StudyReport
        {
            Period = period,
            Days = BuildDays(period, sessions),
            Sessions = sessions
                .OrderByDescending(session => session.StartedAtUtc)
                .Take(MaxDisplayedSessions)
                .ToArray(),
            Timeline = BuildTimeline(samples, _timeProvider.LocalTimeZone, period),
            TopActivities = BuildTopActivities(samples),
            AttentionEvents = events,
            SessionCount = sessions.Count,
            ActiveSeconds = sessions.Sum(session => session.ActiveSeconds),
            StudySeconds = sessions.Sum(session => session.StudySeconds),
            DistractionSeconds = sessions.Sum(session => session.DistractionSeconds),
            NeutralSeconds = sessions.Sum(session => session.NeutralSeconds),
            OnlineClassSeconds = sessions.Sum(session => session.OnlineClassSeconds),
            BlockedAttemptCount = sessions.Sum(session => session.BlockedAttemptCount),
            FocusedSeconds = sessions.Sum(session => session.FocusedSeconds),
            DistractedSeconds = sessions.Sum(session => session.DistractedSeconds),
            AbsentSeconds = sessions.Sum(session => session.AbsentSeconds),
            DrowsySeconds = sessions.Sum(session => session.DrowsySeconds),
            CameraUnavailableSeconds = sessions.Sum(session => session.CameraUnavailableSeconds),
            MonitoringEligibleSeconds = sessions.Sum(session => session.MonitoringEligibleSeconds),
            ConfidenceAvg = WeightedConfidence(sessions),
            SessionsTruncated = sessions.Count > MaxDisplayedSessions,
            TimelineTruncated = timelineTruncated,
            AttentionEventsTruncated = eventsTruncated,
            ActivityDetailsExpired = !timelineTruncated && sessions.Any(session =>
                HasActivity(session) && !sampleSessionIds.Contains(session.Id)),
            AttentionDetailsExpired = !eventsTruncated && sessions.Any(session =>
                HasAttention(session) && !eventSessionIds.Contains(session.Id))
        };
    }

    private static IReadOnlyList<DailyStudySummary> BuildDays(
        ReportPeriod period,
        IReadOnlyList<StudySession> sessions)
    {
        var sessionsByDate = sessions
            .GroupBy(session => session.StartLocalDate)
            .ToDictionary(group => group.Key, group => group.ToArray());

        var days = new DailyStudySummary[period.DayCount];

        for (var index = 0; index < days.Length; index++)
        {
            var date = period.Start.AddDays(index);

            if (!sessionsByDate.TryGetValue(date, out var daySessions))
            {
                days[index] = DailyStudySummary.Empty(date);
                continue;
            }

            days[index] = new DailyStudySummary
            {
                LocalDate = date,
                SessionCount = daySessions.Length,
                ActiveSeconds = daySessions.Sum(session => session.ActiveSeconds),
                StudySeconds = daySessions.Sum(session => session.StudySeconds),
                DistractionSeconds = daySessions.Sum(session => session.DistractionSeconds),
                NeutralSeconds = daySessions.Sum(session => session.NeutralSeconds),
                OnlineClassSeconds = daySessions.Sum(session => session.OnlineClassSeconds),
                BlockedAttemptCount = daySessions.Sum(session => session.BlockedAttemptCount),
                ActiveSessionStartedAtUtc = daySessions
                    .Where(session => session.IsActive)
                    .Select(session => (DateTime?)session.StartedAtUtc)
                    .Max(),
                FocusedSeconds = daySessions.Sum(session => session.FocusedSeconds),
                DistractedSeconds = daySessions.Sum(session => session.DistractedSeconds),
                AbsentSeconds = daySessions.Sum(session => session.AbsentSeconds),
                DrowsySeconds = daySessions.Sum(session => session.DrowsySeconds),
                CameraUnavailableSeconds = daySessions.Sum(session => session.CameraUnavailableSeconds),
                MonitoringEligibleSeconds = daySessions.Sum(session => session.MonitoringEligibleSeconds)
            };
        }

        return days;
    }

    private static IReadOnlyList<HourlyActivityBucket> BuildTimeline(
        IReadOnlyList<ReportActivitySample> samples,
        TimeZoneInfo timeZone,
        ReportPeriod period)
    {
        var buckets = new Dictionary<(DateOnly Date, int Hour), (int Study, int Distraction, int Neutral)>();

        foreach (var sample in samples)
        {
            if (sample.ActiveSeconds <= 0)
                continue;

            // Samples are stored in UTC; the conversion applies the historical
            // DST rules of the selected zone, including a repeated local hour.
            var local = TimeZoneInfo.ConvertTimeFromUtc(AsUtc(sample.SampledAtUtc), timeZone);
            var localDate = DateOnly.FromDateTime(local);
            if (!period.Contains(localDate))
                continue;

            var key = (localDate, local.Hour);

            buckets.TryGetValue(key, out var totals);

            switch (sample.Category)
            {
                case ActivityCategory.Study:
                    totals.Study += sample.ActiveSeconds;
                    break;
                case ActivityCategory.Distraction:
                    totals.Distraction += sample.ActiveSeconds;
                    break;
                default:
                    totals.Neutral += sample.ActiveSeconds;
                    break;
            }

            buckets[key] = totals;
        }

        return buckets
            .OrderBy(bucket => bucket.Key.Date)
            .ThenBy(bucket => bucket.Key.Hour)
            .Select(bucket => new HourlyActivityBucket(
                bucket.Key.Date,
                bucket.Key.Hour,
                bucket.Value.Study,
                bucket.Value.Distraction,
                bucket.Value.Neutral))
            .Where(bucket => bucket.TotalSeconds > 0)
            .ToArray();
    }

    private static IReadOnlyList<TopActivityItem> BuildTopActivities(
        IReadOnlyList<ReportActivitySample> samples)
    {
        var totals = new Dictionary<(string Name, TopActivityKind Kind), int>();

        foreach (var sample in samples)
        {
            if (sample.ActiveSeconds <= 0)
                continue;

            string name;
            TopActivityKind kind;

            if (sample.Domain is { } domain)
            {
                name = domain.Value;
                kind = TopActivityKind.Domain;
            }
            else if (!string.IsNullOrWhiteSpace(sample.AppName))
            {
                name = sample.AppName;
                kind = TopActivityKind.App;
            }
            else
            {
                name = "Không rõ";
                kind = TopActivityKind.Unknown;
            }

            var key = (name, kind);
            totals[key] = totals.GetValueOrDefault(key) + sample.ActiveSeconds;
        }

        return totals
            .OrderByDescending(pair => pair.Value)
            .ThenBy(pair => pair.Key.Name, StringComparer.OrdinalIgnoreCase)
            .Take(TopActivityLimit)
            .Select(pair => new TopActivityItem(pair.Key.Name, pair.Key.Kind, pair.Value))
            .ToArray();
    }

    private static double? WeightedConfidence(IReadOnlyList<StudySession> sessions)
    {
        var weightedSum = 0.0;
        var weight = 0;

        foreach (var session in sessions)
        {
            if (session.ConfidenceAvg is not { } confidence || session.MonitoredSeconds <= 0)
                continue;

            weightedSum += confidence * session.MonitoredSeconds;
            weight += session.MonitoredSeconds;
        }

        return weight > 0 ? weightedSum / weight : null;
    }

    private static bool HasActivity(StudySession session) =>
        session.StudySeconds + session.DistractionSeconds + session.NeutralSeconds > 0;

    private static bool HasAttention(StudySession session) =>
        session.MonitoredSeconds + session.CameraUnavailableSeconds > 0;

    private static DateTime AsUtc(DateTime value) =>
        value.Kind == DateTimeKind.Utc ? value : DateTime.SpecifyKind(value, DateTimeKind.Utc);
}
