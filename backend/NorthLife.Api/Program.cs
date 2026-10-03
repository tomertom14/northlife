using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Diagnostics.HealthChecks;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.RateLimiting;
using Microsoft.AspNetCore.HttpOverrides;
using Microsoft.EntityFrameworkCore;
using Microsoft.IdentityModel.Tokens;
using Microsoft.AspNetCore.DataProtection;
using NorthLife.Api.Analytics;
using NorthLife.Api.Authentication;
using NorthLife.Api.Data;
using NorthLife.Api.Email;
using NorthLife.Api.Health;
using NorthLife.Api.Identity;
using NorthLife.Api.Images;
using NorthLife.Api.Models;
using NorthLife.Api.Services;
using System.Text;
using System.Text.Json.Serialization;
using System.Threading.RateLimiting;
using System.Diagnostics;
using Prometheus;

// Offline evaluation of the recommender on simulated traffic; needs no database or configuration.
if (args.Contains("--evaluate-recommendations", StringComparer.OrdinalIgnoreCase))
{
    var position = Array.FindIndex(args, argument => string.Equals(argument, "--evaluate-recommendations", StringComparison.OrdinalIgnoreCase));
    var output = position + 1 < args.Length && !args[position + 1].StartsWith("--", StringComparison.Ordinal) ? args[position + 1] : "recommendation-evaluation.md";
    var report = NorthLife.Api.Recommendations.OfflineEvaluation.Run();
    var markdown = report.ToMarkdown();
    File.WriteAllText(output, markdown);
    Console.WriteLine(markdown);
    return;
}

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
builder.Services.AddScoped<AccountService>();
builder.Services.AddScoped<UserTokenService>();
builder.Services.AddSingleton<SecondFactorThrottle>();
builder.Services.AddScoped<TotpService>();
builder.Services.AddSingleton<IdentityTickets>();
builder.Services.AddMemoryCache();
builder.Services.AddSingleton<ISessionValidator, SessionValidator>();
builder.Services.AddScoped<AuditLog>();
builder.Services.AddScoped<AdminUserService>();

builder.Services.Configure<AnalyticsOptions>(builder.Configuration.GetSection(AnalyticsOptions.SectionName));
builder.Services.AddSingleton<AnalyticsMetrics>();
builder.Services.AddScoped<AnalyticsIngestService>();
builder.Services.AddScoped<AnalyticsRollupService>();
builder.Services.AddScoped<OwnerAnalyticsService>();
builder.Services.AddScoped<DemoSeeder>();
builder.Services.Configure<NorthLife.Api.Ranking.RankingOptions>(builder.Configuration.GetSection(NorthLife.Api.Ranking.RankingOptions.SectionName));
builder.Services.AddScoped<NorthLife.Api.Ranking.PositionBiasService>();
builder.Services.Configure<NorthLife.Api.Recommendations.RecommendationOptions>(builder.Configuration.GetSection(NorthLife.Api.Recommendations.RecommendationOptions.SectionName));
builder.Services.AddSingleton<NorthLife.Api.Recommendations.RecommendationModelCache>();
builder.Services.AddScoped<NorthLife.Api.Recommendations.RecommendationModelService>();
builder.Services.AddScoped<NorthLife.Api.Recommendations.RecommendationService>();
if (builder.Configuration.GetValue($"{AnalyticsOptions.SectionName}:WorkerEnabled", true))
{
    builder.Services.AddHostedService<AnalyticsWorker>();
}
builder.Services.Configure<NorthLife.Api.Moderation.AutoModerationOptions>(builder.Configuration.GetSection(NorthLife.Api.Moderation.AutoModerationOptions.SectionName));
builder.Services.AddScoped<NorthLife.Api.Moderation.AutoModerationService>();
builder.Services.AddScoped<NorthLife.Api.Moderation.AutoModerationAdminService>();
if (builder.Configuration.GetValue($"{NorthLife.Api.Moderation.AutoModerationOptions.SectionName}:WorkerEnabled", true))
{
    builder.Services.AddHostedService<NorthLife.Api.Moderation.AutoModerationWorker>();
}
builder.Services.Configure<MetricsAccessOptions>(builder.Configuration.GetSection(MetricsAccessOptions.SectionName));

// Data Protection encrypts TOTP secrets and sign-in tickets. Keys must survive restarts and deploys.
var dataProtection = builder.Services.AddDataProtection().SetApplicationName("NorthLife");
var keysPath = builder.Configuration["DataProtection:KeysPath"];
if (!string.IsNullOrWhiteSpace(keysPath))
{
    dataProtection.PersistKeysToFileSystem(Directory.CreateDirectory(keysPath));
}

builder.Services.Configure<GoogleOptions>(builder.Configuration.GetSection(GoogleOptions.SectionName));
builder.Services.AddSingleton<IGoogleTokenValidator, GoogleTokenValidator>();

builder.Services.Configure<EmailOptions>(builder.Configuration.GetSection(EmailOptions.SectionName));
builder.Services.AddScoped<AccountEmails>();
switch (builder.Configuration[$"{EmailOptions.SectionName}:Provider"]?.Trim().ToLowerInvariant())
{
    case "smtp":
        builder.Services.AddScoped<IEmailSender, SmtpEmailSender>();
        break;
    case "brevo":
        builder.Services.AddHttpClient<IEmailSender, BrevoEmailSender>();
        break;
    default:
        builder.Services.AddScoped<IEmailSender, LogEmailSender>();
        break;
}
builder.Services.AddSingleton<IImageStorage, LocalImageStorage>();
builder.Services.AddSingleton<EventImageProcessor>();
builder.Services.AddScoped<EventImageService>();
builder.Services.AddScoped<AdminBootstrapper>();
builder.Services.AddScoped<EventLifecycleService>();
builder.Services.AddScoped<OwnerEventService>();
builder.Services.AddScoped<AdminEventService>();
builder.Services.AddSingleton<EventTimeWindowFactory>();
builder.Services.AddScoped<PublicEventQueryService>();
builder.Services.AddScoped<NorthLife.Api.Places.PlaceLifecycle>();
builder.Services.AddScoped<NorthLife.Api.Places.OwnerPlaceService>();
builder.Services.AddScoped<NorthLife.Api.Places.AdminPlaceService>();
builder.Services.AddScoped<NorthLife.Api.Places.PlaceQueryService>();
builder.Services.AddScoped<NorthLife.Api.Places.DemoPlacesSeeder>();
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
            // Signature and expiry are valid; now reject tokens of suspended accounts or rotated stamps.
            OnTokenValidated = async context =>
            {
                var principal = context.Principal;
                var subject = principal?.FindFirst(System.IdentityModel.Tokens.Jwt.JwtRegisteredClaimNames.Sub)?.Value;
                var stamp = principal?.FindFirst(AuthTokenService.StampClaim)?.Value;
                var validator = context.HttpContext.RequestServices.GetRequiredService<ISessionValidator>();
                if (!Guid.TryParse(subject, out var userId) ||
                    string.IsNullOrEmpty(stamp) ||
                    !await validator.IsCurrentAsync(userId, stamp, context.HttpContext.RequestAborted))
                {
                    context.Fail("session_revoked");
                }
            },
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
                // An administrator whose session skipped TOTP gets a specific code the client can act on.
                var needsMfa =
                    context.HttpContext.User.IsInRole(nameof(UserRole.Admin)) &&
                    context.HttpContext.User.FindFirst(AuthTokenService.MfaClaim)?.Value != "true";
                context.Response.StatusCode = StatusCodes.Status403Forbidden;
                context.Response.ContentType = "application/problem+json";
                await context.Response.WriteAsJsonAsync(new
                {
                    type = "https://httpstatuses.com/403",
                    title = needsMfa ? "נדרש אימות דו-שלבי לחשבון מנהל." : "אין הרשאה לפעולה הזו.",
                    status = StatusCodes.Status403Forbidden,
                    code = needsMfa ? "mfa_required" : "forbidden",
                    traceId = context.HttpContext.TraceIdentifier,
                });
            },
        };
    });
// Administrators must have passed the TOTP step in this session, not only hold the role.
builder.Services.AddAuthorizationBuilder()
    .AddPolicy(AuthPolicies.AdminWithMfa, policy => policy
        .RequireRole(nameof(UserRole.Admin))
        .RequireClaim(AuthTokenService.MfaClaim, "true"));

// Compress the Angular bundle and other static text. JSON stays uncompressed on purpose: sign-in responses
// carry tokens, and compressing secrets next to attacker-influenced content is what BREACH exploits.
builder.Services.AddResponseCompression(options =>
{
    options.EnableForHttps = true;
    options.Providers.Add<Microsoft.AspNetCore.ResponseCompression.BrotliCompressionProvider>();
    options.Providers.Add<Microsoft.AspNetCore.ResponseCompression.GzipCompressionProvider>();
    options.MimeTypes = ["text/html", "text/css", "text/javascript", "application/javascript", "image/svg+xml", "application/manifest+json"];
});
builder.Services.Configure<Microsoft.AspNetCore.ResponseCompression.BrotliCompressionProviderOptions>(
    options => options.Level = System.IO.Compression.CompressionLevel.Optimal);
builder.Services.Configure<Microsoft.AspNetCore.ResponseCompression.GzipCompressionProviderOptions>(
    options => options.Level = System.IO.Compression.CompressionLevel.Optimal);

var authPermitsPerMinute = builder.Configuration.GetValue("RateLimiting:AuthPermitsPerMinute", 10);
var emailPermitsPerWindow = builder.Configuration.GetValue("RateLimiting:EmailPermitsPer15Minutes", 5);
var analyticsPermitsPerMinute = builder.Configuration.GetValue("RateLimiting:AnalyticsPermitsPerMinute", 120);
builder.Services.AddRateLimiter(options =>
{
    options.RejectionStatusCode = StatusCodes.Status429TooManyRequests;
    options.AddPolicy("auth", httpContext =>
        RateLimitPartition.GetFixedWindowLimiter(
            httpContext.Connection.RemoteIpAddress?.ToString() ?? "unknown",
            _ => new FixedWindowRateLimiterOptions
            {
                PermitLimit = authPermitsPerMinute,
                Window = TimeSpan.FromMinutes(1),
                QueueLimit = 0,
                QueueProcessingOrder = QueueProcessingOrder.OldestFirst,
            }));
    // Anything that sends an email: resend verification and forgot password.
    options.AddPolicy("email", httpContext =>
        RateLimitPartition.GetFixedWindowLimiter(
            httpContext.Connection.RemoteIpAddress?.ToString() ?? "unknown",
            _ => new FixedWindowRateLimiterOptions
            {
                PermitLimit = emailPermitsPerWindow,
                Window = TimeSpan.FromMinutes(15),
                QueueLimit = 0,
                QueueProcessingOrder = QueueProcessingOrder.OldestFirst,
            }));
    // Browsers send a batch every few seconds at most; this leaves room for several tabs behind one address.
    options.AddPolicy("analytics", httpContext =>
        RateLimitPartition.GetFixedWindowLimiter(
            httpContext.Connection.RemoteIpAddress?.ToString() ?? "unknown",
            _ => new FixedWindowRateLimiterOptions
            {
                PermitLimit = analyticsPermitsPerMinute,
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

if (args.Contains("--seed-demo", StringComparer.OrdinalIgnoreCase))
{
    await using var scope = app.Services.CreateAsyncScope();
    await scope.ServiceProvider.GetRequiredService<AppDbContext>().Database.MigrateAsync();
    var result = await scope.ServiceProvider.GetRequiredService<DemoSeeder>().SeedAsync(CancellationToken.None);
    if (result is null)
    {
        app.Logger.LogInformation("Demo data already present; nothing to do.");
    }
    else
    {
        app.Logger.LogInformation(
            "Demo data: {Owners} owners (owner1..owner{Owners}@demo.northlife.local, password {Password}), {Events} events, {Interactions} simulated interactions from {Visitors} visitors.",
            result.Owners, result.Owners, result.OwnerPassword, result.Events, result.Interactions, result.Visitors);
    }

    // Also adds places to a demo database seeded before places existed.
    var places = await scope.ServiceProvider.GetRequiredService<NorthLife.Api.Places.DemoPlacesSeeder>().SeedAsync(CancellationToken.None);
    if (places > 0) app.Logger.LogInformation("Demo data: {Places} places added.", places);

    return;
}

if (args.Contains("--refresh-demo", StringComparer.OrdinalIgnoreCase))
{
    await using var scope = app.Services.CreateAsyncScope();
    await scope.ServiceProvider.GetRequiredService<AppDbContext>().Database.MigrateAsync();
    var result = await scope.ServiceProvider.GetRequiredService<DemoSeeder>().RefreshAsync(CancellationToken.None);
    if (result is null)
    {
        app.Logger.LogError("No demo catalogue to refresh; run --seed-demo first.");
        Environment.ExitCode = 1;
        return;
    }

    app.Logger.LogInformation(
        "Demo refreshed: {Events} catalogue events now run from {FirstDay:yyyy-MM-dd} to {LastDay:yyyy-MM-dd}, with {Interactions} simulated interactions from {Visitors} visitors in place of their old traffic.",
        result.Events, result.FirstDay, result.LastDay, result.Interactions, result.Visitors);

    // The web service rebuilds the model every 15 minutes; rebuilding now makes "similar events" current at once.
    try
    {
        var neighbours = await scope.ServiceProvider.GetRequiredService<NorthLife.Api.Recommendations.RecommendationModelService>().RefreshAsync(CancellationToken.None);
        app.Logger.LogInformation("Recommendation model rebuilt with {NeighbourCount} neighbours.", neighbours);
    }
    catch (Exception exception) when (exception is DbUpdateException or Npgsql.PostgresException)
    {
        app.Logger.LogWarning(exception, "The recommendation model was not rebuilt; the web service rebuilds it within 15 minutes.");
    }

    return;
}

if (args.Contains("--seed-load", StringComparer.OrdinalIgnoreCase))
{
    // "--seed-load 10000": extra events for performance tests.
    var position = Array.FindIndex(args, argument => string.Equals(argument, "--seed-load", StringComparison.OrdinalIgnoreCase));
    var count = position + 1 < args.Length && int.TryParse(args[position + 1], out var parsed) ? parsed : 10_000;
    await using var scope = app.Services.CreateAsyncScope();
    await scope.ServiceProvider.GetRequiredService<AppDbContext>().Database.MigrateAsync();
    var added = await scope.ServiceProvider.GetRequiredService<DemoSeeder>().SeedLoadAsync(count, CancellationToken.None);
    app.Logger.LogInformation("Added {EventCount} load-test events.", added);
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

// Google Identity Services asks for this referrer policy when a site is tested on plain
// http://localhost (local QA). Deployed hosts keep the browser default.
app.Use(async (context, next) =>
{
    if (context.Request.Host.Host is "localhost" or "127.0.0.1")
    {
        context.Response.Headers["Referrer-Policy"] = "no-referrer-when-downgrade";
    }

    await next();
});

if (app.Environment.IsDevelopment())
{
    app.MapOpenApi();
    app.UseCors();
}

app.UseHttpsRedirection();
app.UseResponseCompression();
app.UseDefaultFiles();
app.UseStaticFiles(new StaticFileOptions { OnPrepareResponse = StaticCaching.Apply });
app.UseRateLimiter();
app.UseAuthentication();
app.UseAuthorization();
app.UseHttpMetrics();
app.UseWhen(
    context => context.Request.Path.StartsWithSegments("/metrics"),
    branch => branch.Use(async (context, next) =>
    {
        // Operational metrics are not public: a bearer token, or a private network when allowed.
        if (MetricsAccess.IsAllowed(context, context.RequestServices.GetRequiredService<Microsoft.Extensions.Options.IOptions<MetricsAccessOptions>>().Value)) await next();
        else context.Response.StatusCode = StatusCodes.Status404NotFound;
    }));

app.MapControllers();
app.MapMetrics("/metrics");
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
app.MapFallbackToFile("index.html", new StaticFileOptions { OnPrepareResponse = StaticCaching.Apply });

app.Run();

public partial class Program;
