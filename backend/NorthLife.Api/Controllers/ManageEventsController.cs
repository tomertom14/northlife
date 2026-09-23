using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using NorthLife.Api.Contracts;
using NorthLife.Api.Models;
using NorthLife.Api.Services;
using System.IdentityModel.Tokens.Jwt;
using System.Security.Claims;

namespace NorthLife.Api.Controllers;

[ApiController]
[Route("api/manage/events")]
[Authorize(Roles = nameof(UserRole.BusinessOwner))]
public sealed class ManageEventsController(OwnerEventService service) : ControllerBase
{
    [HttpGet]
    public async Task<ActionResult<IReadOnlyList<OwnerEventResponse>>> List(
        CancellationToken cancellationToken) =>
        Ok(await service.ListAsync(UserId(), cancellationToken));

    [HttpGet("{id:guid}")]
    public async Task<ActionResult<OwnerEventResponse>> Get(
        Guid id,
        CancellationToken cancellationToken)
    {
        var result = await service.GetAsync(UserId(), id, cancellationToken);
        return result is null ? NotFound() : Ok(result);
    }

    [HttpPost]
    public async Task<ActionResult<OwnerEventResponse>> Create(
        OwnerEventUpsertRequest request,
        CancellationToken cancellationToken) =>
        await Execute(
            () => service.CreateAsync(UserId(), request, cancellationToken),
            StatusCodes.Status201Created);

    [HttpPut("{id:guid}")]
    public async Task<ActionResult<OwnerEventResponse>> Update(
        Guid id,
        OwnerEventUpsertRequest request,
        CancellationToken cancellationToken) =>
        await Execute(
            () => service.UpdateAsync(UserId(), id, request, cancellationToken),
            StatusCodes.Status200OK);

    [HttpDelete("{id:guid}")]
    public async Task<IActionResult> Delete(
        Guid id,
        [FromQuery] int revision,
        CancellationToken cancellationToken)
    {
        try
        {
            await service.DeleteAsync(UserId(), id, revision, cancellationToken);
            return NoContent();
        }
        catch (OwnerEventNotFoundException)
        {
            return NotFound();
        }
        catch (EventRevisionConflictException)
        {
            return ConflictProblem();
        }
        catch (EventLifecycleException exception)
        {
            return ValidationProblem("event", exception.Message);
        }
    }

    private async Task<ActionResult<OwnerEventResponse>> Execute(
        Func<Task<OwnerEventResponse>> operation,
        int successStatus)
    {
        try
        {
            return StatusCode(successStatus, await operation());
        }
        catch (OwnerEventValidationException exception)
        {
            var details = new ValidationProblemDetails(
                exception.Errors.ToDictionary(pair => pair.Key, pair => pair.Value))
            {
                Status = StatusCodes.Status400BadRequest,
                Title = "Event details are invalid.",
            };
            details.Extensions["code"] = "invalid_event";
            return BadRequest(details);
        }
        catch (EventLifecycleException exception)
        {
            return ValidationProblem("event", exception.Message);
        }
        catch (OwnerEventNotFoundException)
        {
            return NotFound();
        }
        catch (Images.ImageNotFoundException)
        {
            return NotFound();
        }
        catch (EventRevisionConflictException)
        {
            return ConflictProblem();
        }
    }

    private ObjectResult ValidationProblem(string field, string message)
    {
        var details = new ValidationProblemDetails(new Dictionary<string, string[]>
        {
            [field] = [message],
        })
        {
            Status = StatusCodes.Status400BadRequest,
            Title = "Event details are invalid.",
        };
        details.Extensions["code"] = "invalid_event";
        return BadRequest(details);
    }

    private ObjectResult ConflictProblem()
    {
        var problem = new ProblemDetails
        {
            Status = StatusCodes.Status409Conflict,
            Title = "The event changed. Reload it before saving again.",
        };
        problem.Extensions["code"] = "revision_conflict";
        return Conflict(problem);
    }

    private Guid UserId() =>
        Guid.Parse(User.FindFirstValue(JwtRegisteredClaimNames.Sub)!);
}
