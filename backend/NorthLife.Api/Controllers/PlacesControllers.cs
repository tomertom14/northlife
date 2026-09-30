using System.IdentityModel.Tokens.Jwt;
using System.Security.Claims;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using NorthLife.Api.Contracts;
using NorthLife.Api.Models;
using NorthLife.Api.Places;
using NorthLife.Api.Services;

namespace NorthLife.Api.Controllers;

/// <summary>The public places directory and place pages.</summary>
[ApiController]
[Route("api/places")]
public sealed class PlacesController(PlaceQueryService queries) : ControllerBase
{
    [HttpGet]
    public Task<ActionResult<PagedResponse<PlaceSummaryResponse>>> List(
        [FromQuery] PublicPlaceQueryParameters query, CancellationToken cancellationToken) =>
        Execute(() => queries.GetPageAsync(query, cancellationToken));

    [HttpGet("map")]
    public Task<ActionResult<MapPlacesResponse>> Map(
        [FromQuery] MapPlaceQueryParameters query, CancellationToken cancellationToken) =>
        Execute(() => queries.GetMapAsync(query, cancellationToken));

    [HttpGet("{id:guid}")]
    public async Task<ActionResult<PlaceDetailsResponse>> Get(Guid id, CancellationToken cancellationToken)
    {
        var place = await queries.GetDetailsAsync(id, cancellationToken);
        if (place is not null) return Ok(place);
        return NotFound(new ProblemDetails
        {
            Status = StatusCodes.Status404NotFound,
            Title = "Place unavailable",
            Detail = "The place does not exist or is not publicly available.",
            Extensions = { ["code"] = "place_unavailable" },
        });
    }

    private async Task<ActionResult<T>> Execute<T>(Func<Task<T>> query)
    {
        try
        {
            return Ok(await query());
        }
        catch (PlaceQueryValidationException exception)
        {
            var details = new ValidationProblemDetails(new Dictionary<string, string[]> { [exception.Field] = [exception.Message] })
            {
                Status = StatusCodes.Status400BadRequest,
                Title = "Invalid place query",
            };
            details.Extensions["code"] = "invalid_place_query";
            return BadRequest(details);
        }
    }
}

/// <summary>A business owner's own places.</summary>
[ApiController]
[Route("api/manage/places")]
[Authorize(Roles = nameof(UserRole.BusinessOwner))]
public sealed class ManagePlacesController(OwnerPlaceService service) : ControllerBase
{
    [HttpGet]
    public async Task<ActionResult<IReadOnlyList<OwnerPlaceResponse>>> List(CancellationToken cancellationToken) =>
        Ok(await service.ListAsync(UserId(), cancellationToken));

    [HttpGet("{id:guid}")]
    public async Task<ActionResult<OwnerPlaceResponse>> Get(Guid id, CancellationToken cancellationToken)
    {
        var place = await service.GetAsync(UserId(), id, cancellationToken);
        return place is null ? NotFound() : Ok(place);
    }

    [HttpPost]
    public Task<ActionResult<OwnerPlaceResponse>> Create(OwnerPlaceUpsertRequest request, CancellationToken cancellationToken) =>
        PlaceResults.Execute(this, () => service.CreateAsync(UserId(), request, cancellationToken), StatusCodes.Status201Created);

    [HttpPut("{id:guid}")]
    public Task<ActionResult<OwnerPlaceResponse>> Update(Guid id, OwnerPlaceUpsertRequest request, CancellationToken cancellationToken) =>
        PlaceResults.Execute(this, () => service.UpdateAsync(UserId(), id, request, cancellationToken));

    [HttpDelete("{id:guid}")]
    public async Task<IActionResult> Delete(Guid id, [FromQuery] int revision, CancellationToken cancellationToken)
    {
        try
        {
            await service.DeleteAsync(UserId(), id, revision, cancellationToken);
            return NoContent();
        }
        catch (PlaceNotFoundException) { return NotFound(); }
        catch (PlaceRevisionConflictException) { return PlaceResults.Conflict(this); }
        catch (PlaceLifecycleException exception) { return PlaceResults.Invalid(this, exception.Message); }
    }

    private Guid UserId() => Guid.Parse(User.FindFirstValue(JwtRegisteredClaimNames.Sub)!);
}

/// <summary>Moderation of places.</summary>
[ApiController]
[Route("api/admin/places")]
[Authorize(Policy = Authentication.AuthPolicies.AdminWithMfa)]
public sealed class AdminPlacesController(AdminPlaceService service) : ControllerBase
{
    [HttpGet]
    public async Task<ActionResult<IReadOnlyList<AdminPlaceResponse>>> List(
        [FromQuery] EventStatus? status, [FromQuery] string? search, CancellationToken cancellationToken)
    {
        try { return Ok(await service.ListAsync(status, search, cancellationToken)); }
        catch (PlaceValidationException exception) { return PlaceResults.Invalid(this, exception); }
    }

    [HttpPost("{id:guid}/approve")]
    public Task<ActionResult<AdminPlaceResponse>> Approve(Guid id, AdminPlaceRevisionRequest request, CancellationToken cancellationToken) =>
        PlaceResults.Execute(this, () => service.ApproveAsync(UserId(), id, request.Revision, cancellationToken));

    [HttpPost("{id:guid}/reject")]
    public Task<ActionResult<AdminPlaceResponse>> Reject(Guid id, AdminPlaceRejectRequest request, CancellationToken cancellationToken) =>
        PlaceResults.Execute(this, () => service.RejectAsync(UserId(), id, request.Revision, request.Reason, cancellationToken));

    [HttpDelete("{id:guid}")]
    public async Task<IActionResult> Delete(Guid id, [FromQuery] int revision, CancellationToken cancellationToken)
    {
        try
        {
            await service.DeleteAsync(UserId(), id, revision, cancellationToken);
            return NoContent();
        }
        catch (PlaceNotFoundException) { return NotFound(); }
        catch (PlaceRevisionConflictException) { return PlaceResults.Conflict(this); }
        catch (PlaceLifecycleException exception) { return PlaceResults.Invalid(this, exception.Message); }
    }

    private Guid UserId() => Guid.Parse(User.FindFirstValue(JwtRegisteredClaimNames.Sub)!);
}

/// <summary>Shared error mapping for place changes, in the same shape as the event endpoints.</summary>
internal static class PlaceResults
{
    public static async Task<ActionResult<T>> Execute<T>(ControllerBase controller, Func<Task<T>> operation, int status = StatusCodes.Status200OK)
    {
        try
        {
            return controller.StatusCode(status, await operation());
        }
        catch (PlaceValidationException exception) { return Invalid(controller, exception); }
        catch (PlaceLifecycleException exception) { return Invalid(controller, exception.Message); }
        catch (PlaceNotFoundException) { return controller.NotFound(); }
        catch (Images.ImageNotFoundException) { return controller.NotFound(); }
        catch (PlaceRevisionConflictException) { return Conflict(controller); }
        catch (EmailNotConfirmedException)
        {
            var problem = new ProblemDetails
            {
                Status = StatusCodes.Status403Forbidden,
                Title = "יש לאמת את כתובת האימייל לפני שליחת מקומות.",
            };
            problem.Extensions["code"] = "email_not_verified";
            return controller.StatusCode(StatusCodes.Status403Forbidden, problem);
        }
    }

    public static ObjectResult Invalid(ControllerBase controller, PlaceValidationException exception)
    {
        var details = new ValidationProblemDetails(exception.Errors.ToDictionary(pair => pair.Key, pair => pair.Value))
        {
            Status = StatusCodes.Status400BadRequest,
            Title = "Place details are invalid.",
        };
        details.Extensions["code"] = "invalid_place";
        return controller.BadRequest(details);
    }

    public static ObjectResult Invalid(ControllerBase controller, string message) =>
        Invalid(controller, new PlaceValidationException(new Dictionary<string, string[]> { ["place"] = [message] }));

    public static ObjectResult Conflict(ControllerBase controller)
    {
        var problem = new ProblemDetails
        {
            Status = StatusCodes.Status409Conflict,
            Title = "The place changed. Reload it before continuing.",
        };
        problem.Extensions["code"] = "revision_conflict";
        return controller.Conflict(problem);
    }
}
