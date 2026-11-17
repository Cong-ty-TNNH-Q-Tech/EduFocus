using System.Text.Json;
using EduFocus.Cloud.Api.Auth;
using EduFocus.Cloud.Api.Common;
using EduFocus.Cloud.Api.RateLimiting;
using EduFocus.Cloud.Contracts.Common;
using EduFocus.Cloud.Contracts.Family;
using EduFocus.Cloud.Contracts.Json;
using EduFocus.Cloud.Data;
using Microsoft.EntityFrameworkCore;

namespace EduFocus.Cloud.Api.Families;

public static class FamilyEndpoints
{
    public const string ConfirmationText = "XÓA";

    public static IEndpointRouteBuilder MapFamilyEndpoints(this IEndpointRouteBuilder endpoints)
    {
        endpoints.MapDelete("/family", DeleteAsync)
            .RequireAuthorization(AuthorizationPolicies.FamilyAdmin)
            .RequireRateLimiting(RateLimitPolicies.Token);

        return endpoints;
    }

    private static async Task<IResult> DeleteAsync(
        HttpContext httpContext,
        CloudDbContext db,
        CancellationToken cancellationToken)
    {
        DeleteFamilyRequest? request;
        try
        {
            request = await httpContext.Request.ReadFromJsonAsync<DeleteFamilyRequest>(
                ApiJson.Options, cancellationToken);
        }
        catch (JsonException)
        {
            return ApiResults.InvalidRequest("Body không đúng định dạng JSON.");
        }

        if (request?.Confirmation != ConfirmationText)
        {
            return ApiResults.Error(
                StatusCodes.Status400BadRequest,
                "confirmation_required",
                $"Nhập đúng chữ {ConfirmationText} để xác nhận xóa toàn bộ dữ liệu cloud.");
        }

        var familyId = httpContext.User.FamilyId();
        var now = DateTimeOffset.UtcNow;

        await using var transaction = await db.Database.BeginTransactionAsync(cancellationToken);
        var deleted = await db.Families
            .Where(f => f.Id == familyId)
            .ExecuteDeleteAsync(cancellationToken);
        if (deleted == 0)
        {
            await transaction.RollbackAsync(cancellationToken);
            return ApiResults.NotFound();
        }

        // The family is gone; keep an anonymous audit trail without family or device identifiers.
        db.CloudAuditLogs.Add(CloudAudit.Create(null, null, "FamilyDeleted", now));
        await db.SaveChangesAsync(cancellationToken);
        await transaction.CommitAsync(cancellationToken);

        return Results.Ok(new OperationResponse(true));
    }
}
