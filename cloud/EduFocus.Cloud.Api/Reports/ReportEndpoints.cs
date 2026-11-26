using EduFocus.Cloud.Api.Auth;
using EduFocus.Cloud.Api.Common;
using EduFocus.Cloud.Api.RateLimiting;
using EduFocus.Cloud.Contracts.Reports;
using EduFocus.Cloud.Data;
using Microsoft.EntityFrameworkCore;

namespace EduFocus.Cloud.Api.Reports;

public static class ReportEndpoints
{
    public const int MaxRangeDays = 31;

    public static IEndpointRouteBuilder MapReportEndpoints(this IEndpointRouteBuilder endpoints)
    {
        endpoints.MapGet("/reports/daily", GetDailyAsync)
            .RequireAuthorization(AuthorizationPolicies.Read)
            .RequireRateLimiting(RateLimitPolicies.Token);

        return endpoints;
    }

    private static async Task<IResult> GetDailyAsync(
        DateOnly? from,
        DateOnly? to,
        CloudDbContext db,
        HttpContext httpContext,
        CancellationToken cancellationToken)
    {
        if (from is null
            || to is null
            || from > to
            || to.Value.DayNumber - from.Value.DayNumber + 1 > MaxRangeDays)
        {
            return ApiResults.Error(
                StatusCodes.Status400BadRequest,
                "invalid_range",
                $"Khoảng ngày phải hợp lệ và tối đa {MaxRangeDays} ngày.");
        }

        var familyId = httpContext.User.FamilyId();
        var family = await db.Families
            .AsNoTracking()
            .FirstOrDefaultAsync(f => f.Id == familyId, cancellationToken);
        if (family is null || family.DeletedAtUtc is not null)
        {
            return ApiResults.NotFound();
        }

        var summaries = await db.DailySummaries
            .AsNoTracking()
            .Where(s => s.FamilyId == familyId
                && s.LocalDate >= from.Value
                && s.LocalDate <= to.Value)
            .OrderByDescending(s => s.UpdatedAtUtc)
            .ToListAsync(cancellationToken);

        var days = summaries
            .GroupBy(s => s.LocalDate)
            .OrderBy(group => group.Key)
            .Select(group => AggregateDay(group, family.TimeZoneId))
            .ToList();

        return Results.Ok(days);
    }

    private static DailySummaryDto AggregateDay(
        IGrouping<DateOnly, EduFocus.Cloud.Data.Entities.DailySummary> group,
        string timeZoneId)
    {
        var list = group.ToList();
        if (list.Count == 1)
        {
            var single = list[0];
            return new DailySummaryDto(
                single.LocalDate,
                timeZoneId,
                single.UpdatedAtUtc,
                single.SessionsCount,
                single.ActiveSeconds,
                single.StudySeconds,
                single.IdleSeconds,
                single.OnlineClassSeconds,
                single.MonitoringEligibleSeconds,
                single.MonitoredSeconds,
                single.FocusedSeconds,
                single.DistractedSeconds,
                single.AbsentSeconds,
                single.DrowsySeconds,
                single.BlockedAttempts,
                single.AttentionPercent,
                single.CoveragePercent);
        }

        var updatedAtUtc = list.Max(s => s.UpdatedAtUtc);
        var sessionsCount = list.Sum(s => s.SessionsCount);
        var activeSeconds = list.Sum(s => s.ActiveSeconds);
        var studySeconds = list.Sum(s => s.StudySeconds);
        var idleSeconds = list.Sum(s => s.IdleSeconds);
        var onlineClassSeconds = list.Sum(s => s.OnlineClassSeconds);
        var monitoringEligibleSeconds = list.Sum(s => s.MonitoringEligibleSeconds);
        var monitoredSeconds = list.Sum(s => s.MonitoredSeconds);
        var focusedSeconds = list.Sum(s => s.FocusedSeconds);
        var distractedSeconds = list.Sum(s => s.DistractedSeconds);
        var absentSeconds = list.Sum(s => s.AbsentSeconds);
        var drowsySeconds = list.Sum(s => s.DrowsySeconds);
        var blockedAttempts = list.Sum(s => s.BlockedAttempts);

        double? attentionPercent = monitoredSeconds > 0
            ? Math.Round((focusedSeconds * 100.0) / monitoredSeconds, 1)
            : null;
        double? coveragePercent = monitoringEligibleSeconds > 0
            ? Math.Round((monitoredSeconds * 100.0) / monitoringEligibleSeconds, 1)
            : null;

        return new DailySummaryDto(
            group.Key,
            timeZoneId,
            updatedAtUtc,
            sessionsCount,
            activeSeconds,
            studySeconds,
            idleSeconds,
            onlineClassSeconds,
            monitoringEligibleSeconds,
            monitoredSeconds,
            focusedSeconds,
            distractedSeconds,
            absentSeconds,
            drowsySeconds,
            blockedAttempts,
            attentionPercent,
            coveragePercent);
    }
}
