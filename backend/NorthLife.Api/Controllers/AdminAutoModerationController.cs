using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using NorthLife.Api.Contracts;
using NorthLife.Api.Moderation;
using System.IdentityModel.Tokens.Jwt;
using System.Security.Claims;

namespace NorthLife.Api.Controllers;

[ApiController]
[Route("api/admin/auto-moderation")]
[Authorize(Policy = NorthLife.Api.Authentication.AuthPolicies.AdminWithMfa)]
public sealed class AdminAutoModerationController(
    AutoModerationAdminService admin,
    AutoModerationService service) : ControllerBase
{
    [HttpGet]
    public async Task<ActionResult<AutoModerationOverview>> Get(CancellationToken cancellationToken) =>
        Ok(await admin.GetOverviewAsync(cancellationToken));

    [HttpPut("settings")]
    public async Task<ActionResult<AutoModerationOverview>> UpdateSettings(
        AutoModerationSettingsRequest request,
        CancellationToken cancellationToken)
    {
        try
        {
            return Ok(await admin.UpdateSettingsAsync(UserId(), request, cancellationToken));
        }
        catch (AutoModerationSettingsException exception)
        {
            var details = new ValidationProblemDetails(exception.Errors.ToDictionary(pair => pair.Key, pair => pair.Value))
            { Status = 400, Title = "Automatic approval settings are invalid." };
            details.Extensions["code"] = "invalid_settings";
            return BadRequest(details);
        }
    }

    [HttpPost("run")]
    public async Task<ActionResult<AutoModerationRunSummary>> Run(CancellationToken cancellationToken)
    {
        try
        {
            return await service.RunManualAsync(UserId(), cancellationToken) is { } summary
                ? Ok(summary)
                : Problem409("run_in_progress", "Another automatic approval run is in progress.");
        }
        catch (AutoModerationOffException)
        {
            return Problem409("mode_off", "Automatic approval is switched off.");
        }
    }

    private ObjectResult Problem409(string code, string title)
    {
        var details = new ProblemDetails { Status = 409, Title = title };
        details.Extensions["code"] = code;
        return Conflict(details);
    }

    private Guid UserId() => Guid.Parse(User.FindFirstValue(JwtRegisteredClaimNames.Sub)!);
}
