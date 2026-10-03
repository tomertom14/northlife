using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using NorthLife.Api.Authentication;
using NorthLife.Api.Contracts;
using NorthLife.Api.Models;
using NorthLife.Api.Services;
using System.IdentityModel.Tokens.Jwt;
using System.Security.Claims;

namespace NorthLife.Api.Controllers;

[ApiController]
[Route("api/admin")]
[Authorize(Policy = AuthPolicies.AdminWithMfa)]
public sealed class AdminUsersController(AdminUserService service) : ControllerBase
{
    [HttpGet("users")]
    public async Task<ActionResult<AdminUserPage>> List(
        [FromQuery] string? search,
        [FromQuery] UserRole? role,
        [FromQuery] string? status,
        [FromQuery] string? cursor,
        [FromQuery] int pageSize = AdminUserService.DefaultPageSize,
        CancellationToken cancellationToken = default)
    {
        try
        {
            return Ok(await service.ListAsync(search, role, status, cursor, pageSize, cancellationToken));
        }
        catch (AdminUserRuleException exception)
        {
            return RuleProblem(exception.Code);
        }
    }

    [HttpGet("users/{id:guid}")]
    public async Task<ActionResult<AdminUserDetails>> Get(Guid id, CancellationToken cancellationToken)
    {
        var details = await service.GetAsync(id, cancellationToken);
        return details is null ? NotFound() : Ok(details);
    }

    [HttpPost("users/{id:guid}/suspend")]
    public Task<IActionResult> Suspend(Guid id, SuspendUserRequest request, CancellationToken cancellationToken) =>
        Run(() => service.SuspendAsync(ActorId(), id, request.Reason, cancellationToken));

    [HttpPost("users/{id:guid}/unsuspend")]
    public Task<IActionResult> Unsuspend(Guid id, CancellationToken cancellationToken) =>
        Run(() => service.UnsuspendAsync(ActorId(), id, cancellationToken));

    [HttpPost("users/{id:guid}/role")]
    public Task<IActionResult> ChangeRole(Guid id, ChangeRoleRequest request, CancellationToken cancellationToken) =>
        Run(() => service.ChangeRoleAsync(ActorId(), id, request.Role, cancellationToken));

    [HttpGet("audit")]
    public async Task<ActionResult<AuditPage>> Audit(
        [FromQuery] string? cursor,
        [FromQuery] int pageSize = AdminUserService.DefaultPageSize,
        CancellationToken cancellationToken = default) =>
        Ok(await service.AuditAsync(cursor, pageSize, cancellationToken));

    private async Task<IActionResult> Run(Func<Task> action)
    {
        try
        {
            await action();
            return NoContent();
        }
        catch (AdminUserNotFoundException)
        {
            return NotFound();
        }
        catch (AdminUserRuleException exception)
        {
            return RuleProblem(exception.Code);
        }
    }

    private ObjectResult RuleProblem(string code)
    {
        var (status, title) = code switch
        {
            "cannot_suspend_self" => (StatusCodes.Status400BadRequest, "אי אפשר להשעות את החשבון שלכם."),
            "cannot_change_own_role" => (StatusCodes.Status400BadRequest, "אי אפשר לשנות את התפקיד של עצמכם."),
            "last_admin" => (StatusCodes.Status409Conflict, "חייב להישאר לפחות מנהל פעיל אחד."),
            "concurrent_change" => (StatusCodes.Status409Conflict, "שינוי אחר בוצע באותו רגע. נסו שוב."),
            "reason_required" => (StatusCodes.Status400BadRequest, "כתבו סיבה להשעיה, עד 500 תווים."),
            _ => (StatusCodes.Status400BadRequest, "הפעולה אינה אפשרית."),
        };
        var problem = new ProblemDetails { Status = status, Title = title };
        problem.Extensions["code"] = code;
        return StatusCode(status, problem);
    }

    private Guid ActorId() => Guid.Parse(User.FindFirstValue(JwtRegisteredClaimNames.Sub)!);
}
