using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using NorthLife.Api.Contracts;
using NorthLife.Api.Models;
using NorthLife.Api.Services;
using System.IdentityModel.Tokens.Jwt;
using System.Security.Claims;

namespace NorthLife.Api.Controllers;

[ApiController]
[Route("api/admin/events")]
[Authorize(Roles = nameof(UserRole.Admin))]
public sealed class AdminEventsController(AdminEventService service) : ControllerBase
{
    [HttpGet]
    public async Task<ActionResult<IReadOnlyList<AdminEventResponse>>> List(
        [FromQuery] EventStatus? status, [FromQuery] string? search, CancellationToken cancellationToken)
    {
        try { return Ok(await service.ListAsync(status, search, cancellationToken)); }
        catch (AdminEventValidationException exception) { return Invalid(exception.Message); }
    }

    [HttpPost]
    public Task<ActionResult<AdminEventResponse>> Create(OwnerEventUpsertRequest request, CancellationToken cancellationToken) =>
        Execute(() => service.CreateAsync(UserId(), request, cancellationToken), StatusCodes.Status201Created);

    [HttpPut("{id:guid}")]
    public Task<ActionResult<AdminEventResponse>> Update(Guid id, OwnerEventUpsertRequest request, CancellationToken cancellationToken) =>
        Execute(() => service.UpdateAsync(id, request, cancellationToken));

    [HttpPost("{id:guid}/approve")]
    public Task<ActionResult<AdminEventResponse>> Approve(Guid id, RevisionRequest request, CancellationToken cancellationToken) =>
        Execute(() => service.ApproveAsync(id, request.Revision, cancellationToken));

    [HttpPost("{id:guid}/reject")]
    public Task<ActionResult<AdminEventResponse>> Reject(Guid id, RejectEventRequest request, CancellationToken cancellationToken) =>
        Execute(() => service.RejectAsync(id, request.Revision, request.Reason, cancellationToken));

    [HttpPost("{id:guid}/highlight")]
    public Task<ActionResult<AdminEventResponse>> Highlight(Guid id, HighlightEventRequest request, CancellationToken cancellationToken) =>
        Execute(() => service.HighlightAsync(id, request.Revision, request.IsHighlighted, cancellationToken));

    [HttpDelete("{id:guid}")]
    public async Task<IActionResult> Delete(Guid id, [FromQuery] int revision, CancellationToken cancellationToken)
    {
        try { await service.DeleteAsync(id, revision, cancellationToken); return NoContent(); }
        catch (AdminEventNotFoundException) { return NotFound(); }
        catch (EventRevisionConflictException) { return ConflictResult(); }
        catch (EventLifecycleException exception) { return Invalid(exception.Message); }
    }

    private async Task<ActionResult<AdminEventResponse>> Execute(
        Func<Task<AdminEventResponse>> operation, int status = StatusCodes.Status200OK)
    {
        try { return StatusCode(status, await operation()); }
        catch (OwnerEventValidationException exception)
        {
            var details = new ValidationProblemDetails(exception.Errors.ToDictionary(pair => pair.Key, pair => pair.Value))
            { Status = 400, Title = "Event details are invalid." };
            details.Extensions["code"] = "invalid_event";
            return BadRequest(details);
        }
        catch (EventLifecycleException exception) { return Invalid(exception.Message); }
        catch (AdminEventNotFoundException) { return NotFound(); }
        catch (Images.ImageNotFoundException) { return NotFound(); }
        catch (EventRevisionConflictException) { return ConflictResult(); }
    }

    private ObjectResult Invalid(string message)
    {
        var details = new ValidationProblemDetails(new Dictionary<string, string[]> { ["event"] = [message] })
        { Status = 400, Title = "Event operation is invalid." };
        details.Extensions["code"] = "invalid_event";
        return BadRequest(details);
    }

    private ObjectResult ConflictResult()
    {
        var details = new ProblemDetails { Status = 409, Title = "The event changed. Reload it before continuing." };
        details.Extensions["code"] = "revision_conflict";
        return Conflict(details);
    }

    private Guid UserId() => Guid.Parse(User.FindFirstValue(JwtRegisteredClaimNames.Sub)!);
}
