using System.Security.Claims;
using EduFocus.Cloud.Data.Entities;

namespace EduFocus.Cloud.Api.Auth;

public static class DevicePrincipalExtensions
{
    public static Guid DeviceId(this ClaimsPrincipal principal) =>
        Guid.Parse(principal.FindFirstValue("device_id")
            ?? throw new InvalidOperationException("Missing device_id claim."));

    public static Guid FamilyId(this ClaimsPrincipal principal) =>
        Guid.Parse(principal.FindFirstValue("family_id")
            ?? throw new InvalidOperationException("Missing family_id claim."));

    public static DeviceKind DeviceKind(this ClaimsPrincipal principal) =>
        Enum.Parse<DeviceKind>(principal.FindFirstValue("device_kind")
            ?? throw new InvalidOperationException("Missing device_kind claim."));

    public static string Scopes(this ClaimsPrincipal principal) =>
        string.Join(',', principal.FindAll(DeviceScopes.ClaimType).Select(claim => claim.Value));
}
