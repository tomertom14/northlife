using System.Security.Claims;
using System.Text.RegularExpressions;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.RateLimiting;
using NorthLife.Api.Analytics;
using NorthLife.Api.Authentication;
using NorthLife.Api.Models;
using System.IdentityModel.Tokens.Jwt;

namespace NorthLife.Api.Controllers;

public sealed record TrackInteractionRequest(Guid EventId, InteractionType Type, InteractionSource? Source, int? Position, string? Context = null);

public sealed record TrackRequest(Guid VisitorId, IReadOnlyList<TrackInteractionRequest>? Interactions);

public sealed record TrackResponse(int Received, int Recorded);

public sealed record ForgetVisitorRequest(Guid VisitorId);

[ApiController]
public sealed partial class AnalyticsController(
    AnalyticsIngestService ingest,
    OwnerAnalyticsService analytics,
    Ranking.PositionBiasService positionBias) : ControllerBase
{
    /// <summary>Batched interactions from the public site. Anonymous; rate-limited per client address.</summary>
    [HttpPost("api/analytics/events")]
    [AllowAnonymous]
    [EnableRateLimiting("analytics")]
    public async Task<ActionResult<TrackResponse>> Track(TrackRequest request, CancellationToken cancellationToken)
    {
        var interactions = request.Interactions ?? [];
        if (request.VisitorId == Guid.Empty || interactions.Count > AnalyticsIngestService.MaxBatchSize)
        {
            return ValidationProblem(new ValidationProblemDetails(new Dictionary<string, string[]>
            {
                ["interactions"] = [$"Send a visitor id and at most {AnalyticsIngestService.MaxBatchSize} interactions."],
            }));
        }

        var valid = interactions
            .Where(interaction => interaction.EventId != Guid.Empty && Enum.IsDefined(interaction.Type))
            .Select(interaction => new TrackedInteraction(
                interaction.EventId,
                interaction.Type,
                interaction.Source is { } source && Enum.IsDefined(source) ? source : InteractionSource.Direct,
                interaction.Position,
                interaction.Context))
            .ToList();

        // Crawlers that run scripts would otherwise count as visitors.
        if (IsCrawler(Request.Headers.UserAgent.ToString())) return Accepted(new TrackResponse(interactions.Count, 0));

        var recorded = await ingest.RecordAsync(request.VisitorId, valid, cancellationToken);
        return Accepted(new TrackResponse(interactions.Count, recorded));
    }

    /// <summary>"Reset my history": deletes the raw interactions of this browser's visitor id.</summary>
    [HttpPost("api/analytics/forget")]
    [AllowAnonymous]
    [EnableRateLimiting("analytics")]
    public async Task<IActionResult> Forget(ForgetVisitorRequest request, CancellationToken cancellationToken)
    {
        if (request.VisitorId == Guid.Empty) return BadRequest();
        await ingest.ForgetAsync(request.VisitorId, cancellationToken);
        return NoContent();
    }

    [HttpGet("api/manage/analytics")]
    [Authorize(Roles = nameof(UserRole.BusinessOwner))]
    public async Task<ActionResult<OwnerAnalyticsResponse>> Owner([FromQuery] int days = 30, CancellationToken cancellationToken = default) =>
        Ok(await analytics.GetAsync(Guid.Parse(User.FindFirstValue(JwtRegisteredClaimNames.Sub)!), days, cancellationToken));

    [HttpGet("api/admin/analytics/anomalies")]
    [Authorize(Policy = AuthPolicies.AdminWithMfa)]
    public async Task<ActionResult<IReadOnlyList<TrafficAnomaly>>> Anomalies(CancellationToken cancellationToken) =>
        Ok(await analytics.AnomaliesAsync(cancellationToken));

    /// <summary>The fitted examination propensity per feed position next to the naive click-through ratio.</summary>
    [HttpGet("api/admin/analytics/position-bias")]
    [Authorize(Policy = AuthPolicies.AdminWithMfa)]
    public async Task<ActionResult<IReadOnlyList<PositionPropensityRow>>> PositionBias(CancellationToken cancellationToken) =>
        Ok(await positionBias.CurrentAsync(cancellationToken));

    private static bool IsCrawler(string userAgent) => CrawlerPattern().IsMatch(userAgent);

    [GeneratedRegex(@"bot\b|crawl|spider|slurp|facebookexternalhit|preview", RegexOptions.IgnoreCase)]
    private static partial Regex CrawlerPattern();
}
