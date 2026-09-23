using Microsoft.AspNetCore.Mvc;
using NorthLife.Api.Contracts;
using NorthLife.Api.Services;

namespace NorthLife.Api.Controllers;

[ApiController]
[Route("api/events")]
public sealed class EventsController(PublicEventQueryService queryService) : ControllerBase
{
    [HttpGet]
    [ProducesResponseType<PagedResponse<EventSummaryResponse>>(StatusCodes.Status200OK)]
    public Task<ActionResult<PagedResponse<EventSummaryResponse>>> Get(
        [FromQuery] PublicEventQueryParameters query,
        CancellationToken cancellationToken) =>
        Execute(
            () => queryService.GetPageAsync(query, cancellationToken));

    [HttpGet("today")]
    [ProducesResponseType<PagedResponse<EventSummaryResponse>>(StatusCodes.Status200OK)]
    public Task<ActionResult<PagedResponse<EventSummaryResponse>>> GetToday(
        [FromQuery] EventFilterParameters filters,
        CancellationToken cancellationToken) =>
        Execute(
            () => queryService.GetPageAsync(
                filters.WithPeriod("today"),
                cancellationToken));

    [HttpGet("tomorrow")]
    [ProducesResponseType<PagedResponse<EventSummaryResponse>>(StatusCodes.Status200OK)]
    public Task<ActionResult<PagedResponse<EventSummaryResponse>>> GetTomorrow(
        [FromQuery] EventFilterParameters filters,
        CancellationToken cancellationToken) =>
        Execute(
            () => queryService.GetPageAsync(
                filters.WithPeriod("tomorrow"),
                cancellationToken));

    [HttpGet("from-to")]
    [ProducesResponseType<PagedResponse<EventSummaryResponse>>(StatusCodes.Status200OK)]
    public Task<ActionResult<PagedResponse<EventSummaryResponse>>> GetFromTo(
        [FromQuery] DateOnly? from,
        [FromQuery] DateOnly? to,
        [FromQuery] EventFilterParameters filters,
        CancellationToken cancellationToken) =>
        Execute(
            () => queryService.GetPageAsync(
                filters.WithPeriod("range", from, to),
                cancellationToken));

    [HttpGet("top-picks")]
    [ProducesResponseType<IReadOnlyList<EventSummaryResponse>>(StatusCodes.Status200OK)]
    public Task<ActionResult<IReadOnlyList<EventSummaryResponse>>> GetTopPicks(
        [FromQuery] EventFilterParameters filters,
        CancellationToken cancellationToken) =>
        Execute(
            () => queryService.GetTopPicksAsync(
                filters.WithPeriod("today"),
                cancellationToken));

    [HttpGet("map")]
    [ProducesResponseType<MapEventsResponse>(StatusCodes.Status200OK)]
    public Task<ActionResult<MapEventsResponse>> GetMap(
        [FromQuery] MapEventQueryParameters query,
        CancellationToken cancellationToken) =>
        Execute(() => queryService.GetMapAsync(query, cancellationToken));

    [HttpGet("{id:guid}")]
    [ProducesResponseType<EventDetailsResponse>(StatusCodes.Status200OK)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status404NotFound)]
    public async Task<ActionResult<EventDetailsResponse>> GetById(
        Guid id,
        CancellationToken cancellationToken)
    {
        var eventDetails = await queryService.GetDetailsAsync(id, cancellationToken);
        if (eventDetails is not null)
        {
            return Ok(eventDetails);
        }

        return NotFound(new ProblemDetails
        {
            Title = "Event unavailable",
            Detail = "The event does not exist or is no longer publicly available.",
            Status = StatusCodes.Status404NotFound,
            Extensions = { ["code"] = "event_unavailable" },
        });
    }

    private async Task<ActionResult<T>> Execute<T>(Func<Task<T>> query)
    {
        try
        {
            return Ok(await query());
        }
        catch (PublicEventQueryValidationException exception)
        {
            var details = new ValidationProblemDetails(
                new Dictionary<string, string[]>
                {
                    [exception.Field] = [exception.Message],
                })
            {
                Title = "Invalid event query",
                Status = StatusCodes.Status400BadRequest,
            };
            details.Extensions["code"] = "invalid_event_query";
            details.Extensions["traceId"] = HttpContext.TraceIdentifier;
            return BadRequest(details);
        }
    }
}
