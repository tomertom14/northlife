using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.RateLimiting;
using NorthLife.Api.Contracts;
using NorthLife.Api.Recommendations;
using NorthLife.Api.Services;

namespace NorthLife.Api.Controllers;

[ApiController]
[AllowAnonymous]
[EnableRateLimiting("analytics")]
public sealed class RecommendationsController(RecommendationService recommendations) : ControllerBase
{
    /// <summary>
    /// "For you": events picked from this browser's anonymous history within the feed's current
    /// filters; popular events for a visitor with no history.
    /// </summary>
    [HttpGet("api/recommendations")]
    public async Task<ActionResult<RecommendationsResponse>> ForYou(
        [FromQuery] Guid? visitorId,
        [FromQuery] PublicEventQueryParameters filters,
        [FromQuery] int limit = 10,
        CancellationToken cancellationToken = default)
    {
        try
        {
            // Without an explicit period the recommendations cover everything coming up.
            var scoped = Request.Query.ContainsKey("period") ? filters : null;
            return Ok(await recommendations.ForYouAsync(visitorId, scoped, limit, cancellationToken));
        }
        catch (PublicEventQueryValidationException exception)
        {
            return ValidationProblem(new ValidationProblemDetails(new Dictionary<string, string[]> { [exception.Field] = [exception.Message] }));
        }
    }

    /// <summary>"More like this": the event's nearest neighbours that are still coming up.</summary>
    [HttpGet("api/events/{id:guid}/similar")]
    public async Task<ActionResult<IReadOnlyList<EventSummaryResponse>>> Similar(Guid id, [FromQuery] int limit = 6, CancellationToken cancellationToken = default) =>
        Ok(await recommendations.SimilarAsync(id, limit, cancellationToken));
}
