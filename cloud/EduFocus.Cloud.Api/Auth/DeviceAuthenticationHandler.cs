using System.Security.Claims;
using System.Security.Cryptography;
using System.Text;
using System.Text.Encodings.Web;
using EduFocus.Cloud.Data;
using Microsoft.AspNetCore.Authentication;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;

namespace EduFocus.Cloud.Api.Auth;

public sealed class DeviceAuthenticationHandler(
    IOptionsMonitor<AuthenticationSchemeOptions> options,
    ILoggerFactory logger,
    UrlEncoder encoder,
    CloudDbContext db)
    : AuthenticationHandler<AuthenticationSchemeOptions>(options, logger, encoder)
{
    protected override async Task<AuthenticateResult> HandleAuthenticateAsync()
    {
        var header = Request.Headers.Authorization.ToString();
        if (!header.StartsWith("Bearer ", StringComparison.OrdinalIgnoreCase))
        {
            return AuthenticateResult.NoResult();
        }

        var token = header["Bearer ".Length..].Trim();
        if (!DeviceTokenService.TryHash(token, out var hash))
        {
            return AuthenticateResult.Fail("invalid_token");
        }

        var now = DateTimeOffset.UtcNow;
        var tokenEntity = await db.DeviceTokens
            .AsNoTracking()
            .FirstOrDefaultAsync(t => t.TokenHash == hash && t.RevokedAtUtc == null, Context.RequestAborted);
        if (tokenEntity is null)
        {
            return AuthenticateResult.Fail("invalid_token");
        }

        if (!CryptographicOperations.FixedTimeEquals(
                Encoding.ASCII.GetBytes(hash),
                Encoding.ASCII.GetBytes(tokenEntity.TokenHash)))
        {
            return AuthenticateResult.Fail("invalid_token");
        }

        var device = await db.Devices
            .AsNoTracking()
            .FirstOrDefaultAsync(d => d.Id == tokenEntity.DeviceId, Context.RequestAborted);
        if (device is null || device.RevokedAtUtc is not null)
        {
            return AuthenticateResult.Fail("device_revoked");
        }

        var family = await db.Families
            .AsNoTracking()
            .FirstOrDefaultAsync(f => f.Id == device.FamilyId, Context.RequestAborted);
        if (family is null || family.DeletedAtUtc is not null)
        {
            return AuthenticateResult.Fail("family_deleted");
        }

        if (tokenEntity.LastUsedAtUtc is null
            || now - tokenEntity.LastUsedAtUtc.Value >= TimeSpan.FromMinutes(1))
        {
            await db.DeviceTokens
                .Where(t => t.Id == tokenEntity.Id)
                .ExecuteUpdateAsync(
                    setters => setters.SetProperty(t => t.LastUsedAtUtc, now),
                    Context.RequestAborted);
        }

        var claims = new List<Claim>
        {
            new(ClaimTypes.NameIdentifier, device.Id.ToString()),
            new("device_id", device.Id.ToString()),
            new("family_id", family.Id.ToString()),
            new("device_kind", device.Kind.ToString()),
        };
        foreach (var scope in tokenEntity.Scopes.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
        {
            claims.Add(new Claim(DeviceScopes.ClaimType, scope));
        }

        var identity = new ClaimsIdentity(claims, Scheme.Name);
        return AuthenticateResult.Success(new AuthenticationTicket(new ClaimsPrincipal(identity), Scheme.Name));
    }
}
