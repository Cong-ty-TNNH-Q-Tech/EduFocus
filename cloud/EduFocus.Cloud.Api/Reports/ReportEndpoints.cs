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
            .Select(group => group.First())
            .OrderBy(s => s.LocalDate)
            .Select(s => new DailySummaryDto(
                s.LocalDate,
                family.TimeZoneId,
                s.UpdatedAtUtc,
                s.SessionsCount,
                s.ActiveSeconds,
                s.StudySeconds,
                s.IdleSeconds,
                s.OnlineClassSeconds,
                s.MonitoringEligibleSeconds,
                s.MonitoredSeconds,
                s.FocusedSeconds,
                s.DistractedSeconds,
                s.AbsentSeconds,
                s.DrowsySeconds,
                s.BlockedAttempts,
                s.AttentionPercent,
                s.CoveragePercent))
            .ToList();

        return Results.Ok(days);
    }
}
