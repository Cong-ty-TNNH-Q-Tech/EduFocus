using System.Text.Json;
using System.Text.Json.Serialization;
using EduFocus.Cloud.Api.Auth;
using EduFocus.Cloud.Api.Common;
using EduFocus.Cloud.Api.Devices;
using EduFocus.Cloud.Api.Health;
using EduFocus.Cloud.Api.Link;
using EduFocus.Cloud.Api.Logging;
using EduFocus.Cloud.Api.Options;
using EduFocus.Cloud.Api.Pairing;
using EduFocus.Cloud.Api.RateLimiting;
using EduFocus.Cloud.Contracts.Common;
using EduFocus.Cloud.Data;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.HttpOverrides;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Diagnostics.HealthChecks;
using Serilog;
using Serilog.Events;
using Serilog.Formatting.Compact;

var builder = WebApplication.CreateBuilder(args);

builder.Host.UseSerilog((_, _, configuration) => configuration
    .MinimumLevel.Information()
    .MinimumLevel.Override("Microsoft.AspNetCore", LogEventLevel.Warning)
    .Enrich.FromLogContext()
    .Enrich.With<RedactingEnricher>()
    .WriteTo.Console(new CompactJsonFormatter()));

var connectionString = CloudDatabaseConnection.Resolve(
    builder.Configuration.GetConnectionString("Postgres"),
    builder.Environment.EnvironmentName);

builder.Services.AddDbContext<CloudDbContext>(options => options.UseNpgsql(connectionString));
builder.Services.AddProblemDetails();
builder.Services.AddOpenApi();
builder.Services.AddHealthChecks().AddCheck<DatabaseHealthCheck>("postgres");
builder.Services.AddAuthentication(DeviceAuthenticationDefaults.Scheme)
    .AddScheme<AuthenticationSchemeOptions, DeviceAuthenticationHandler>(
        DeviceAuthenticationDefaults.Scheme,
        _ => { });
builder.Services.AddAuthorization(options =>
{
    options.AddPolicy(AuthorizationPolicies.Sync, policy => policy
        .RequireAuthenticatedUser()
        .RequireClaim(DeviceScopes.ClaimType, DeviceScopes.Sync));
    options.AddPolicy(AuthorizationPolicies.Read, policy => policy
        .RequireAuthenticatedUser()
        .RequireClaim(DeviceScopes.ClaimType, DeviceScopes.Read));
    options.AddPolicy(AuthorizationPolicies.Preferences, policy => policy
        .RequireAuthenticatedUser()
        .RequireClaim(DeviceScopes.ClaimType, DeviceScopes.Preferences));
    options.AddPolicy(AuthorizationPolicies.FamilyAdmin, policy => policy
        .RequireAuthenticatedUser()
        .RequireClaim(DeviceScopes.ClaimType, DeviceScopes.FamilyAdmin));
});

var rateLimits = builder.Configuration
    .GetSection(RateLimitOptions.SectionName)
    .Get<RateLimitOptions>() ?? new RateLimitOptions();
builder.Services.AddCloudRateLimiting(rateLimits);

builder.Services.AddOptions<CloudApiOptions>()
    .Bind(builder.Configuration.GetSection(CloudApiOptions.SectionName))
    .PostConfigure(options =>
    {
        var publicUrl = builder.Configuration["EDUFOCUS_CLOUD_PUBLIC_URL"];
        if (!string.IsNullOrWhiteSpace(publicUrl))
        {
            options.PublicBaseUrl = publicUrl.TrimEnd('/');
        }
    });

builder.Services.ConfigureHttpJsonOptions(options =>
{
    options.SerializerOptions.PropertyNamingPolicy = JsonNamingPolicy.CamelCase;
    options.SerializerOptions.DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull;
    options.SerializerOptions.Converters.Add(new JsonStringEnumConverter(JsonNamingPolicy.CamelCase));
});

var app = builder.Build();

if (builder.Configuration.GetValue<bool>("EDUFOCUS_CLOUD_AUTO_MIGRATE"))
{
    await using var scope = app.Services.CreateAsyncScope();
    await scope.ServiceProvider.GetRequiredService<CloudDbContext>().Database.MigrateAsync();
    app.Logger.LogInformation("Applied cloud database migrations");
}

app.UseForwardedHeaders(new ForwardedHeadersOptions
{
    ForwardedHeaders = ForwardedHeaders.XForwardedFor | ForwardedHeaders.XForwardedProto,
});
app.UseExceptionHandler();
app.UseStatusCodePages(async context =>
{
    var (code, message) = context.HttpContext.Response.StatusCode switch
    {
        StatusCodes.Status401Unauthorized => ("unauthorized", "Thiết bị chưa được xác thực."),
        StatusCodes.Status403Forbidden => ("forbidden", "Thiết bị không có quyền truy cập."),
        StatusCodes.Status404NotFound => ("not_found", "Không tìm thấy dữ liệu."),
        StatusCodes.Status405MethodNotAllowed => ("method_not_allowed", "Phương thức không được hỗ trợ."),
        StatusCodes.Status415UnsupportedMediaType => ("unsupported_media_type", "Định dạng nội dung không được hỗ trợ."),
        _ => ("request_failed", "Yêu cầu không hợp lệ."),
    };

    await context.HttpContext.Response.WriteAsJsonAsync(
        new ApiErrorResponse(new ApiError(code, message)));
});
app.UseSerilogRequestLogging();
app.UseRateLimiter();
app.UseAuthentication();
app.UseAuthorization();

if (app.Environment.IsDevelopment())
{
    app.MapOpenApi();
}

app.MapGet("/healthz", async (HealthCheckService healthChecks, CancellationToken cancellationToken) =>
{
    var report = await healthChecks.CheckHealthAsync(cancellationToken);
    var healthy = report.Status == HealthStatus.Healthy;
    return Results.Json(
        new HealthResponse(healthy ? "healthy" : "unhealthy", DateTimeOffset.UtcNow),
        statusCode: healthy ? StatusCodes.Status200OK : StatusCodes.Status503ServiceUnavailable);
});

var v1 = app.MapGroup("/v1");
v1.MapGet("/", () => Results.Ok(new
{
    name = "EduFocus Cloud API",
    version = typeof(Program).Assembly.GetName().Version?.ToString(3) ?? "1.0.0",
}));
v1.MapPairingEndpoints();
v1.MapDeviceEndpoints();
v1.MapLinkEndpoints();
app.Run();

public partial class Program;
