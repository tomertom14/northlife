using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Diagnostics.HealthChecks;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.RateLimiting;
using Microsoft.AspNetCore.HttpOverrides;
using Microsoft.EntityFrameworkCore;
using Microsoft.IdentityModel.Tokens;
using NorthLife.Api.Authentication;
using NorthLife.Api.Data;
using NorthLife.Api.Health;
using NorthLife.Api.Images;
using NorthLife.Api.Models;
using NorthLife.Api.Services;
using System.Text;
using System.Text.Json.Serialization;
using System.Threading.RateLimiting;
using System.Diagnostics;

var builder = WebApplication.CreateBuilder(args);

builder.Logging.ClearProviders();
builder.Logging.AddJsonConsole(options =>
{
    options.IncludeScopes = true;
    options.TimestampFormat = "yyyy-MM-ddTHH:mm:ss.fffZ";
    options.UseUtcTimestamp = true;
});

builder.Services.AddProblemDetails(options =>
{
    options.CustomizeProblemDetails = context =>
    {
        context.ProblemDetails.Extensions["traceId"] = context.HttpContext.TraceIdentifier;
    };
});

builder.Services.AddControllers()
    .AddJsonOptions(options =>
        options.JsonSerializerOptions.Converters.Add(new JsonStringEnumConverter()));
builder.Services.AddOpenApi();

var connectionString = builder.Configuration.GetConnectionString("Database");
var databaseUrl = builder.Configuration["Database:Url"];
if (string.IsNullOrWhiteSpace(connectionString) && !string.IsNullOrWhiteSpace(databaseUrl))
{
    connectionString = DatabaseUrl.ToConnectionString(databaseUrl);
}
if (string.IsNullOrWhiteSpace(connectionString))
{
    throw new InvalidOperationException(
        "ConnectionStrings:Database is required. See .env.example for local configuration.");
}

var jwtOptions = builder.Configuration
    .GetSection(JwtOptions.SectionName)
    .Get<JwtOptions>()
    ?? throw new InvalidOperationException("Authentication configuration is required.");
if (string.IsNullOrWhiteSpace(jwtOptions.JwtKey) ||
    Encoding.UTF8.GetByteCount(jwtOptions.JwtKey) < 32)
{
    throw new InvalidOperationException(
        "Authentication:JwtKey must contain at least 32 UTF-8 bytes.");
}
if (jwtOptions.TokenLifetimeMinutes != 60)
{
    throw new InvalidOperationException(
        "Authentication:TokenLifetimeMinutes must be 60 for version 1.");
}

builder.Services.Configure<JwtOptions>(
    builder.Configuration.GetSection(JwtOptions.SectionName));
builder.Services.Configure<ImageStorageOptions>(
    builder.Configuration.GetSection(ImageStorageOptions.SectionName));
builder.Services.AddDbContext<AppDbContext>(options => options.UseNpgsql(connectionString));
builder.Services.AddSingleton(TimeProvider.System);
builder.Services.AddScoped<IPasswordHasher<AppUser>, PasswordHasher<AppUser>>();
builder.Services.AddScoped<AuthTokenService>();
builder.Services.AddScoped<AuthService>();
builder.Services.AddSingleton<IImageStorage, LocalImageStorage>();
builder.Services.AddSingleton<EventImageProcessor>();
builder.Services.AddScoped<EventImageService>();
builder.Services.AddScoped<AdminBootstrapper>();
builder.Services.AddScoped<EventLifecycleService>();
builder.Services.AddScoped<OwnerEventService>();
builder.Services.AddScoped<AdminEventService>();
builder.Services.AddSingleton<EventTimeWindowFactory>();
builder.Services.AddScoped<PublicEventQueryService>();
builder.Services.AddScoped<DevelopmentDataSeeder>();

builder.Services
    .AddAuthentication(JwtBearerDefaults.AuthenticationScheme)
    .AddJwtBearer(options =>
    {
        options.MapInboundClaims = false;
        options.TokenValidationParameters = new TokenValidationParameters
        {
            ValidateIssuer = true,
            ValidIssuer = jwtOptions.Issuer,
            ValidateAudience = true,
            ValidAudience = jwtOptions.Audience,
            ValidateIssuerSigningKey = true,
            IssuerSigningKey = new SymmetricSecurityKey(
                Encoding.UTF8.GetBytes(jwtOptions.JwtKey)),
            ValidateLifetime = true,
            ClockSkew = TimeSpan.Zero,
            NameClaimType = System.Security.Claims.ClaimTypes.Name,
            RoleClaimType = System.Security.Claims.ClaimTypes.Role,
        };
        options.Events = new JwtBearerEvents
        {
            OnChallenge = async context =>
            {
                context.HandleResponse();
                context.Response.StatusCode = StatusCodes.Status401Unauthorized;
                context.Response.ContentType = "application/problem+json";
                await context.Response.WriteAsJsonAsync(new
                {
                    type = "https://httpstatuses.com/401",
                    title = "נדרשת התחברות.",
                    status = StatusCodes.Status401Unauthorized,
                    code = "invalid_token",
                    traceId = context.HttpContext.TraceIdentifier,
                });
            },
            OnForbidden = async context =>
            {
                context.Response.StatusCode = StatusCodes.Status403Forbidden;
                context.Response.ContentType = "application/problem+json";
                await context.Response.WriteAsJsonAsync(new
                {
                    type = "https://httpstatuses.com/403",
                    title = "אין הרשאה לפעולה הזו.",
                    status = StatusCodes.Status403Forbidden,
                    code = "forbidden",
                    traceId = context.HttpContext.TraceIdentifier,
                });
            },
        };
    });
builder.Services.AddAuthorization();
builder.Services.AddRateLimiter(options =>
{
    options.RejectionStatusCode = StatusCodes.Status429TooManyRequests;
    options.AddPolicy("auth", httpContext =>
        RateLimitPartition.GetFixedWindowLimiter(
            httpContext.Connection.RemoteIpAddress?.ToString() ?? "unknown",
            _ => new FixedWindowRateLimiterOptions
            {
                PermitLimit = 10,
                Window = TimeSpan.FromMinutes(1),
                QueueLimit = 0,
                QueueProcessingOrder = QueueProcessingOrder.OldestFirst,
            }));
    options.OnRejected = async (context, cancellationToken) =>
    {
        context.HttpContext.Response.ContentType = "application/problem+json";
        await context.HttpContext.Response.WriteAsJsonAsync(new
        {
            type = "https://httpstatuses.com/429",
            title = "יותר מדי ניסיונות. נסו שוב בעוד דקה.",
            status = StatusCodes.Status429TooManyRequests,
            code = "rate_limited",
            traceId = context.HttpContext.TraceIdentifier,
        }, cancellationToken);
    };
});
builder.Services
    .AddHealthChecks()
    .AddCheck<DatabaseHealthCheck>("database", tags: ["ready"]);

var allowedOrigins = builder.Configuration.GetSection("Cors:AllowedOrigins").Get<string[]>() ?? [];
builder.Services.AddCors(options =>
{
    options.AddDefaultPolicy(policy =>
    {
        if (allowedOrigins.Length > 0)
        {
            policy.WithOrigins(allowedOrigins).AllowAnyHeader().AllowAnyMethod();
        }
    });
});
builder.Services.Configure<ForwardedHeadersOptions>(options =>
{
    options.ForwardedHeaders = ForwardedHeaders.XForwardedFor | ForwardedHeaders.XForwardedProto;
    options.KnownIPNetworks.Clear();
    options.KnownProxies.Clear();
});

var app = builder.Build();

if (args.Contains("--seed-data", StringComparer.OrdinalIgnoreCase))
{
    await using var scope = app.Services.CreateAsyncScope();
    var dbContext = scope.ServiceProvider.GetRequiredService<AppDbContext>();
    await dbContext.Database.MigrateAsync();
    await scope.ServiceProvider.GetRequiredService<DevelopmentDataSeeder>().SeedAsync();
    return;
}

if (args.Contains("--bootstrap-admin", StringComparer.OrdinalIgnoreCase))
{
    await using var scope = app.Services.CreateAsyncScope();
    var dbContext = scope.ServiceProvider.GetRequiredService<AppDbContext>();
    await dbContext.Database.MigrateAsync();
    await scope.ServiceProvider.GetRequiredService<AdminBootstrapper>().RunAsync();
    return;
}
if (args.Contains("--cleanup-images", StringComparer.OrdinalIgnoreCase))
{
    await using var scope = app.Services.CreateAsyncScope();
    var removed = await scope.ServiceProvider
        .GetRequiredService<EventImageService>()
        .CleanupOrphansAsync(CancellationToken.None);
    app.Logger.LogInformation("Removed {ImageCount} orphaned images.", removed);
    return;
}
if (args.Contains("--migrate", StringComparer.OrdinalIgnoreCase))
{
    await using var scope = app.Services.CreateAsyncScope();
    await scope.ServiceProvider.GetRequiredService<AppDbContext>().Database.MigrateAsync();
    app.Logger.LogInformation("Database migrations applied.");
    return;
}

app.UseForwardedHeaders();
app.Use(async (context, next) =>
{
    var stopwatch = Stopwatch.StartNew();
    try { await next(); }
    finally
    {
        app.Logger.LogInformation(
            "HTTP {Method} {Path} returned {StatusCode} in {ElapsedMilliseconds} ms",
            context.Request.Method,
            context.Request.Path,
            context.Response.StatusCode,
            stopwatch.Elapsed.TotalMilliseconds);
    }
});
app.UseExceptionHandler();

if (app.Environment.IsDevelopment())
{
    app.MapOpenApi();
    app.UseCors();
}

app.UseHttpsRedirection();
app.UseDefaultFiles();
app.UseStaticFiles();
app.UseRateLimiter();
app.UseAuthentication();
app.UseAuthorization();

app.MapControllers();
app.MapHealthChecks("/health/live", new HealthCheckOptions
{
    Predicate = _ => false,
});
app.MapHealthChecks("/health/ready", new HealthCheckOptions
{
    Predicate = check => check.Tags.Contains("ready"),
});
// Unknown API paths are real 404s; only browser routes fall back to the Angular shell.
app.MapFallback("/api/{**path}", () => Results.Problem(
    title: "Not found",
    statusCode: StatusCodes.Status404NotFound,
    extensions: new Dictionary<string, object?> { ["code"] = "not_found" }));
app.MapFallbackToFile("index.html");

app.Run();

public partial class Program;
