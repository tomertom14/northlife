using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.RateLimiting;
using NorthLife.Api.Authentication;
using NorthLife.Api.Contracts;
using NorthLife.Api.Models;
using System.IdentityModel.Tokens.Jwt;
using System.Security.Claims;

namespace NorthLife.Api.Controllers;

[ApiController]
[Route("api/auth")]
public sealed class AuthController(AuthService authService) : ControllerBase
{
    [HttpPost("register")]
    [AllowAnonymous]
    [EnableRateLimiting("auth")]
    public async Task<ActionResult<AuthResponse>> Register(
        RegisterRequest request,
        CancellationToken cancellationToken)
    {
        try
        {
            var response = await authService.RegisterAsync(request, cancellationToken);
            return StatusCode(StatusCodes.Status201Created, response);
        }
        catch (AuthValidationException exception)
        {
            return ValidationProblem(exception.Errors);
        }
        catch (AuthConflictException exception)
        {
            return ConflictProblem(exception.Code, exception.Message);
        }
    }

    [HttpPost("login")]
    [AllowAnonymous]
    [EnableRateLimiting("auth")]
    public async Task<ActionResult<AuthResponse>> Login(
        LoginRequest request,
        CancellationToken cancellationToken)
    {
        try
        {
            return Ok(await authService.LoginAsync(request, cancellationToken));
        }
        catch (LoginFailedException)
        {
            return UnauthorizedProblem(
                "invalid_credentials",
                "כתובת האימייל או הסיסמה אינם נכונים.");
        }
    }

    [HttpPost("logout")]
    [Authorize]
    public IActionResult Logout() => NoContent();

    [HttpGet("session")]
    [Authorize]
    public ActionResult<AuthUserResponse> Session()
    {
        var subject = User.FindFirstValue(JwtRegisteredClaimNames.Sub);
        var role = User.FindFirstValue(ClaimTypes.Role);
        if (!Guid.TryParse(subject, out var id) ||
            !Enum.TryParse<UserRole>(role, out var parsedRole))
        {
            return UnauthorizedProblem("invalid_token", "נדרשת התחברות מחדש.");
        }

        return Ok(new AuthUserResponse(
            id,
            User.FindFirstValue(ClaimTypes.Name) ?? string.Empty,
            User.FindFirstValue(JwtRegisteredClaimNames.Email) ?? string.Empty,
            User.FindFirstValue(AuthTokenService.BusinessNameClaim) ?? string.Empty,
            parsedRole));
    }

    private ObjectResult ValidationProblem(IReadOnlyDictionary<string, string[]> errors)
    {
        var details = new ValidationProblemDetails(
            errors.ToDictionary(pair => pair.Key, pair => pair.Value))
        {
            Status = StatusCodes.Status400BadRequest,
            Title = "פרטי ההרשמה אינם תקינים.",
        };
        details.Extensions["code"] = "invalid_registration";
        return StatusCode(StatusCodes.Status400BadRequest, details);
    }

    private ObjectResult ConflictProblem(string code, string detail)
    {
        var problem = new ProblemDetails
        {
            Status = StatusCodes.Status409Conflict,
            Title = "לא ניתן להשלים את ההרשמה.",
            Detail = detail,
        };
        problem.Extensions["code"] = code;
        return StatusCode(StatusCodes.Status409Conflict, problem);
    }

    private ObjectResult UnauthorizedProblem(string code, string detail)
    {
        var problem = new ProblemDetails
        {
            Status = StatusCodes.Status401Unauthorized,
            Title = "ההתחברות נכשלה.",
            Detail = detail,
        };
        problem.Extensions["code"] = code;
        return StatusCode(StatusCodes.Status401Unauthorized, problem);
    }
}
